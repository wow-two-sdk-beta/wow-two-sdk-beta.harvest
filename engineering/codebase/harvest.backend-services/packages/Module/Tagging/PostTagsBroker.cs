using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Ai.Inferences;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Runs;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Tags posts through the shared AI runner and stores each answer per title-and-text hash.</summary>
internal sealed class PostTagsBroker(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource,
    ModulePostBroker posts,
    IOptions<PostTagsOptions> options,
    IServiceProvider services,
    TimeProvider time,
    ILogger<PostTagsBroker> logger) : IPostTagsBroker
{
    private const int MaxBackfill = 100_000;
    private const int LatencySample = 1000;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromDays(1);

    private (IReadOnlySet<string> Keys, string Rendered)? taxonomy;

    /// <inheritdoc />
    public async Task<Result<PostTagsRunModel>> RunAsync(Guid postId, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        if (services.GetService<IAiRunner>() is null)
            return Result<PostTagsRunModel>.Fail(AppErrorFactory.Conflict("The shared AI runner is not composed in this host."));
        var subject = await PostTagsStore.ReadSubjectAsync(dataSource, postId, cancellationToken);
        if (subject is null || !subject.Current)
            return Result<PostTagsRunModel>.Fail(AppErrorFactory.NotFound("Post not found, or merged or purged."));
        if (!subject.Explicit)
            return Result<PostTagsRunModel>.Fail(AppErrorFactory.Forbidden("A source supplying the post's title or text has model processing off."));
        if (!PostTagsContent.HasContent(subject.Post.Base))
            return Result<PostTagsRunModel>.Fail(AppErrorFactory.Validation("The post has no title or text to tag."));
        return Result<PostTagsRunModel>.Ok(await TagAsync(subject, attempts: 1, dryRun, null, explicitRow: true, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<int>> BackfillAsync(PostTagsBackfillRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limit is < 1 or > MaxBackfill)
            return Result<int>.Fail(AppErrorFactory.Validation($"The limit must be between 1 and {MaxBackfill}."));
        return Result<int>.Ok(await PostTagsStore.BackfillAsync(dataSource, request.SinceUtc?.ToUniversalTime(), request.Limit,
            request.SourceId, time.GetUtcNow(), cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<PostTagsReportModel>> ReportAsync(DateTimeOffset? sinceUtc = null, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var since = (sinceUtc ?? now.AddDays(-7)).ToUniversalTime();
        if (since >= now)
            return Result<PostTagsReportModel>.Fail(AppErrorFactory.Validation("The report must start before now."));
        var report = await PostTagsStore.CountAsync(dataSource, since, cancellationToken);
        var (attempts, cost, p95, sampled) = await InferenceStatsAsync(since, now, cancellationToken);
        return Result<PostTagsReportModel>.Ok(report with { Attempts = attempts, CostMicroUsd = cost, P95LatencyMs = p95, LatencySample = sampled });
    }

    /// <summary>Reads the use case's attempts and cost since a time, and the p95 latency over the newest attempts; all
    /// null when the host composes no inference log.</summary>
    private async Task<(long? Attempts, long? Cost, long? P95, int Sampled)> InferenceStatsAsync(DateTimeOffset since, DateTimeOffset now,
        CancellationToken ct)
    {
        if (services.GetService<IInferencesBroker>() is not { } inferences)
            return (null, null, null, 0);
        long? attempts = null, cost = null;
        if (await inferences.TotalsAsync(since, now.AddSeconds(1), ct) is Result<IReadOnlyList<InferenceTotals>>.Success { Value: var totals })
        {
            var mine = totals.Where(total => total.UseCase == PostTagsConstants.UseCase).ToArray();
            attempts = mine.Sum(total => total.Attempts);
            cost = mine.Sum(total => total.CostMicroUsd);
        }
        long? p95 = null;
        int sampled = 0;
        if (await inferences.ListAsync(new InferenceQuery { UseCase = PostTagsConstants.UseCase, FromUtc = since, Limit = LatencySample },
                ct) is Result<IReadOnlyList<InferenceModel>>.Success { Value: var rows } && rows.Count > 0)
        {
            var latencies = rows.Select(row => row.LatencyMs).Order().ToArray();
            sampled = latencies.Length;
            p95 = latencies[Math.Max(0, (int)Math.Ceiling(latencies.Length * 0.95) - 1)];
        }
        return (attempts, cost, p95, sampled);
    }

    /// <inheritdoc />
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        string owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        var claims = await PostTagsStore.ClaimAsync(dataSource, owner, time.GetUtcNow(), settings.LeaseDuration, settings.BatchSize, cancellationToken);
        foreach (var claim in claims)
        {
            try
            {
                var subject = await PostTagsStore.ReadSubjectAsync(dataSource, claim.PostId, cancellationToken);
                // The mode is read now, not when the row was queued: auto for any row, and for a row an operator named
                // any mode but off.
                if (subject is null || !subject.Current || !subject.Allows(claim.Explicit) || !PostTagsContent.HasContent(subject.Post.Base))
                {
                    await PostTagsStore.DropAsync(dataSource, claim.PostId, cancellationToken);
                    continue;
                }
                // A post whose source sections imply its tags gets them in code, not from a model, unless an operator named it.
                if (!claim.Explicit && await posts.ApplyImpliedTagsAsync(claim.ContainerId, claim.PostId, cancellationToken))
                {
                    await PostTagsStore.DropAsync(dataSource, claim.PostId, cancellationToken);
                    continue;
                }
                await TagAsync(subject, claim.Attempts, dryRun: false, claim.RunId, claim.Explicit, cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "Post tagging failed for post {PostId}; it retries after its lease", claim.PostId);
            }
        }
        return claims.Count;
    }

    private async Task<PostTagsRunModel> TagAsync(PostTagsSubject subject, int attempts, bool dryRun, Guid? runId, bool explicitRow,
        CancellationToken ct)
    {
        var post = subject.Post;
        var hash = PostTagsContent.Hash(post.Base);
        var call = await CallAsync(subject, ct);
        var model = new PostTagsRunModel
        {
            PostId = post.Id, Revision = post.Revision, Status = call.Status, Reason = call.Reason,
            InferenceId = call.Run is { Attempts.Count: > 0 } inferred && inferred.Final.Id != Guid.Empty ? inferred.Final.Id : null,
            Model = call.Run is { Attempts.Count: > 0 } run ? $"{run.Final.Provider}/{run.Final.Model}" : null,
            PromptVersion = call.Run is { Attempts.Count: > 0 } versioned ? versioned.Final.PromptVersion : null,
            FromCache = call.Run?.FromCache ?? false, CostMicroUsd = call.Run is { FromCache: false } paid ? paid.CostMicroUsd : 0,
            Answer = call.Tags is null ? null : PostTagsStore.Element(call.Tags),
        };
        if (dryRun)
            return model;

        var retry = call.Status == "done" ? (DateTimeOffset?)null
            : time.GetUtcNow() + TimeSpan.FromTicks(Math.Min(MaxRetryDelay.Ticks, options.Value.RetryDelay.Ticks * Math.Max(1, attempts)));
        var write = await posts.WriteTagsAsync(new PostTagsRecord(post.ContainerId, post.Id, hash, post.Revision, call.Status, call.Reason,
            model.PromptVersion, call.Run is { Attempts.Count: > 0 } hashed ? hashed.Final.PromptHash : null, model.InferenceId, model.Model,
            call.Tags, retry, runId, explicitRow), ct);
        return model with { Stored = write.Stored, NewRevision = write.NewRevision };
    }

    private async Task<CallResult> CallAsync(PostTagsSubject subject, CancellationToken ct)
    {
        if (services.GetService<IAiRunner>() is not { } runner || services.GetService<IPromptsBroker>() is not { } prompts)
            return CallResult.Unavailable("ai-unavailable");
        var settings = options.Value;
        if (settings.Models.Length == 0)
            return CallResult.Unavailable("no-model");
        if (await prompts.GetUseCaseAsync(PostTagsConstants.UseCase, ct) is not Result<UseCaseModel>.Success { Value: var useCase })
            return CallResult.Unavailable("use-case-missing");
        if (!Compatible(useCase))
            return CallResult.Unavailable("use-case-incompatible");

        taxonomy ??= await PostTagsStore.ReadTaxonomyAsync(dataSource, ct);
        var post = subject.Post;
        var response = await runner.RunAsync(new AiRunRequest
        {
            UseCase = PostTagsConstants.UseCase,
            Subject = $"{PostTagsConstants.SubjectPrefix}{post.Id:D}@{post.Revision}",
            Version = settings.PromptVersion,
            Models = settings.Models,
            Variables = PostTagsContent.Variables(post.Base, SourceCategory(subject), taxonomy.Value.Rendered, settings.MaxTextChars,
                post.FirstSeenAtUtc),
            // The answer's parse maps an out-of-list value to its fallback and flags it (AnswerNormalizer).
            CheckEnums = false,
        }, ct);
        if (response is not Result<AiRunModel>.Success { Value: var run })
            return CallResult.Unavailable("prompt-unavailable");
        if (run.Outcome != AiOutcome.Succeeded)
            return new(LastError(run) is AiErrorKind.NotConfigured or AiErrorKind.Unauthorized ? "unavailable" : "failed", Reason(run), run, null);
        return PostTagsDefinition.Parse(run.Output) is { } answer
            ? new("done", null, run, PostTagsValidation.Validate(answer, taxonomy.Value.Keys))
            : new("failed", "answer-invalid", run, null);
    }

    /// <summary>Whether the registered use case keeps versions and outputs and answers with the code's schema.</summary>
    internal static bool Compatible(UseCaseModel useCase)
    {
        if (useCase is not { PromptMode: PromptMode.Versioned, KeepOutputs: true, OutputSchema: { } schema })
            return false;
        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(schema), JsonNode.Parse(PostTagsDefinition.OutputSchema));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? SourceCategory(PostTagsSubject subject) =>
        subject.Post.Base.SourceTags.Count > 0 ? string.Join(" › ", subject.Post.Base.SourceTags.Take(5)) : subject.Section;

    private static string Reason(AiRunModel run) => run.Outcome switch
    {
        AiOutcome.SchemaInvalid => "schema-invalid",
        AiOutcome.Refused => "refused",
        _ => LastError(run) switch
        {
            AiErrorKind.NotConfigured => "not-configured",
            AiErrorKind.Unauthorized => "unauthorized",
            AiErrorKind.RateLimited => "rate-limited",
            AiErrorKind.Timeout => "timeout",
            AiErrorKind.Network => "network",
            AiErrorKind.Unavailable => "provider-unavailable",
            AiErrorKind.BadRequest => "bad-request",
            _ => "provider-failed",
        },
    };

    private static AiErrorKind? LastError(AiRunModel run) => run.Attempts.Count > 0 ? run.Final.ErrorKind : null;

    private sealed record CallResult(string Status, string? Reason, AiRunModel? Run, ValidatedPostTags? Tags)
    {
        internal static CallResult Unavailable(string reason) => new("unavailable", reason, null, null);
    }
}
