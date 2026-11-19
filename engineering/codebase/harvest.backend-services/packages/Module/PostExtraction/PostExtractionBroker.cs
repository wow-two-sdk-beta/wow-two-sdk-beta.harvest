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
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Extracts tagged posts through the shared AI runner and stores each checked answer per post, content and use case.</summary>
internal sealed class PostExtractionBroker(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource,
    PostExtractionGate gate,
    ModulePostBroker posts,
    IOptions<PostExtractionOptions> options,
    IServiceProvider services,
    TimeProvider time,
    ILogger<PostExtractionBroker> logger) : IPostExtractionBroker
{
    private const int MaxBackfill = 100_000;
    private const int LatencySample = 1000;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromDays(1);

    /// <inheritdoc />
    public async Task<Result<PostExtractionRunModel>> RunAsync(Guid postId, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        if (services.GetService<IAiRunner>() is null)
            return Result<PostExtractionRunModel>.Fail(AppErrorFactory.Conflict("The shared AI runner is not composed in this host."));
        var subject = await PostTagsStore.ReadSubjectAsync(dataSource, postId, cancellationToken);
        if (subject is null || !subject.Current)
            return Result<PostExtractionRunModel>.Fail(AppErrorFactory.NotFound("Post not found, or merged or purged."));
        if (!subject.Explicit)
            return Result<PostExtractionRunModel>.Fail(AppErrorFactory.Forbidden("A source supplying the post's title or text has model processing off."));
        if (!PostTagsContent.HasContent(subject.Post.Base))
            return Result<PostExtractionRunModel>.Fail(AppErrorFactory.Validation("The post has no title or text to extract."));
        var hash = PostTagsContent.Hash(subject.Post.Base);
        if (await PostExtractionStore.ReadTagsAsync(dataSource, postId, hash, cancellationToken) is not { } tags)
            return Result<PostExtractionRunModel>.Fail(AppErrorFactory.Conflict("Tag the post first: its current title and text have no done post-tags answer."));
        if (PostExtractionConstants.UseCaseFor(tags.EntityType) is not { } useCase)
            return Result<PostExtractionRunModel>.Fail(AppErrorFactory.Validation("The post's tags name neither an event nor a listing."));
        return Result<PostExtractionRunModel>.Ok(await ExtractAsync(subject, tags, useCase, hash, attempts: 1, dryRun, null, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<int>> BackfillAsync(PostExtractionBackfillRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limit is < 1 or > MaxBackfill)
            return Result<int>.Fail(AppErrorFactory.Validation($"The limit must be between 1 and {MaxBackfill}."));
        if (!gate.Enabled)
            return Result<int>.Fail(AppErrorFactory.Conflict("Extraction needs the shared AI runner and a demand source in this host."));
        var wanted = await gate.WantedAsync(cancellationToken);
        return Result<int>.Ok(await PostExtractionStore.BackfillAsync(dataSource, wanted, request.SinceUtc?.ToUniversalTime(), request.Limit,
            time.GetUtcNow(), cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<PostExtractionReportModel>> ReportAsync(DateTimeOffset? sinceUtc = null, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var since = (sinceUtc ?? now.AddDays(-7)).ToUniversalTime();
        if (since >= now)
            return Result<PostExtractionReportModel>.Fail(AppErrorFactory.Validation("The report must start before now."));
        var counts = await PostExtractionStore.CountAsync(dataSource, since, cancellationToken);
        if (services.GetService<IInferencesBroker>() is { } inferences)
        {
            IReadOnlyList<InferenceTotals>? totals =
                await inferences.TotalsAsync(since, now.AddSeconds(1), cancellationToken) is Result<IReadOnlyList<InferenceTotals>>.Success { Value: var all }
                    ? all : null;
            foreach (var useCase in PostExtractionConstants.UseCases)
            {
                var report = counts[useCase];
                if (totals is not null)
                {
                    var mine = totals.Where(total => total.UseCase == useCase).ToArray();
                    report = report with { Attempts = mine.Sum(total => total.Attempts), CostMicroUsd = mine.Sum(total => total.CostMicroUsd) };
                }
                if (await inferences.ListAsync(new InferenceQuery { UseCase = useCase, FromUtc = since, Limit = LatencySample }, cancellationToken)
                        is Result<IReadOnlyList<InferenceModel>>.Success { Value: var rows } && rows.Count > 0)
                {
                    var latencies = rows.Select(row => row.LatencyMs).Order().ToArray();
                    report = report with
                    {
                        LatencySample = latencies.Length, P95LatencyMs = latencies[Math.Max(0, (int)Math.Ceiling(latencies.Length * 0.95) - 1)],
                    };
                }
                counts[useCase] = report;
            }
        }
        return Result<PostExtractionReportModel>.Ok(new PostExtractionReportModel { SinceUtc = since, UseCases = counts });
    }

    /// <inheritdoc />
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        string owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        var claims = await PostExtractionStore.ClaimAsync(dataSource, owner, time.GetUtcNow(), settings.LeaseDuration, settings.BatchSize, cancellationToken);
        if (claims.Count == 0)
            return 0;
        var wanted = await gate.WantedAsync(cancellationToken);
        foreach (var claim in claims)
        {
            try
            {
                var subject = await PostTagsStore.ReadSubjectAsync(dataSource, claim.PostId, cancellationToken);
                var hash = subject is null ? null : PostTagsContent.Hash(subject.Post.Base);
                var tags = hash is not null && hash.AsSpan().SequenceEqual(claim.ContentHash)
                    ? await PostExtractionStore.ReadTagsAsync(dataSource, claim.PostId, hash, cancellationToken) : null;
                // The mode is read now: auto for any row, any mode but off for a row an operator named. A re-run's row
                // was chosen for its use case already; it needs only tags of the use case's entity type, not the demand.
                if (subject is not { Current: true } || !subject.Allows(claim.Explicit) || tags is null || !PostTagsContent.HasContent(subject.Post.Base)
                    || (claim.RunId is null
                        ? PostExtractionDemand.Match(wanted, tags.EntityType, tags.Domains)
                        : PostExtractionConstants.UseCaseFor(tags.EntityType)) != claim.UseCase)
                {
                    await PostExtractionStore.DropAsync(dataSource, claim, cancellationToken);
                    continue;
                }
                await ExtractAsync(subject, tags, claim.UseCase, hash!, claim.Attempts, dryRun: false, claim.RunId, cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "Post extraction {UseCase} failed for post {PostId}; it retries after its lease", claim.UseCase, claim.PostId);
            }
        }
        return claims.Count;
    }

    private async Task<PostExtractionRunModel> ExtractAsync(PostTagsSubject subject, ExtractionTags tags, string useCase, byte[] hash, int attempts,
        bool dryRun, Guid? runId, CancellationToken ct)
    {
        var post = subject.Post;
        var call = await CallAsync(subject, tags, useCase, ct);
        var final = call.Run is { Attempts.Count: > 0 } run ? run.Final : null;
        var model = new PostExtractionRunModel
        {
            PostId = post.Id, Revision = post.Revision, UseCase = useCase, Status = call.Status, Reason = call.Reason,
            InferenceId = final is not null && final.Id != Guid.Empty ? final.Id : null,
            Model = final is null ? null : $"{final.Provider}/{final.Model}",
            PromptVersion = final?.PromptVersion,
            FromCache = call.Run?.FromCache ?? false, CostMicroUsd = call.Run is { FromCache: false } paid ? paid.CostMicroUsd : 0,
            Role = call.Answer?.Role, Answer = call.Answer?.Answer,
        };
        if (dryRun)
            return model;

        var retry = call.Status == "done" ? (DateTimeOffset?)null
            : time.GetUtcNow() + TimeSpan.FromTicks(Math.Min(MaxRetryDelay.Ticks, options.Value.RetryDelay.Ticks * Math.Max(1, attempts)));
        var revised = await posts.WriteExtractionAsync(new PostExtractionRecord(post.ContainerId, post.Id, hash, useCase, post.Revision,
            call.Status, call.Reason, model.PromptVersion, final?.PromptHash, model.InferenceId, model.Model, call.Answer, attempts, retry, runId),
            queueEntities: services.GetService<EntityGate>()?.Enabled == true, ct);
        return model with { Stored = true, NewRevision = revised };
    }

    private async Task<CallResult> CallAsync(PostTagsSubject subject, ExtractionTags tags, string useCase, CancellationToken ct)
    {
        if (services.GetService<IAiRunner>() is not { } runner || services.GetService<IPromptsBroker>() is not { } prompts)
            return CallResult.Unavailable("ai-unavailable");
        var settings = options.Value;
        if (settings.Models.Length == 0)
            return CallResult.Unavailable("no-model");
        if (await prompts.GetUseCaseAsync(useCase, ct) is not Result<UseCaseModel>.Success { Value: var registered })
            return CallResult.Unavailable("use-case-missing");
        if (!Compatible(registered))
            return CallResult.Unavailable("use-case-incompatible");

        var post = subject.Post;
        var response = await runner.RunAsync(new AiRunRequest
        {
            UseCase = useCase,
            Subject = $"{PostTagsConstants.SubjectPrefix}{post.Id:D}@{post.Revision}",
            Version = settings.PromptVersions.TryGetValue(useCase, out var pinned) ? pinned : null,
            Models = settings.Models,
            Variables = PostExtractionContent.Variables(post.Base, SourceCategory(subject), tags, settings.MaxTextChars, post.FirstSeenAtUtc),
        }, ct);
        if (response is not Result<AiRunModel>.Success { Value: var run })
            return CallResult.Unavailable("prompt-unavailable");
        if (run.Outcome != AiOutcome.Succeeded)
            return new(LastError(run) is AiErrorKind.NotConfigured or AiErrorKind.Unauthorized ? "unavailable" : "failed", Reason(run), run, null);

        var corpus = PostExtractionContent.Corpus(post.Base, settings.MaxTextChars);
        ValidatedExtraction? answer = useCase == PostExtractionConstants.EventUseCase
            ? PostExtractionDefinitions.Parse<ExtractEventAnswer>(run.Output) is { } parsedEvent
                ? PostExtractionValidation.ValidateEvent(parsedEvent, corpus, tags) : null
            : PostExtractionDefinitions.Parse<ExtractListingAnswer>(run.Output) is { } parsedListing
                ? PostExtractionValidation.ValidateListing(parsedListing, corpus, tags) : null;
        return answer is null ? new("failed", "answer-invalid", run, null) : new("done", null, run, answer);
    }

    /// <summary>Whether the registered use case keeps versions and outputs and answers with the code's schema.</summary>
    internal static bool Compatible(UseCaseModel useCase)
    {
        if (useCase is not { PromptMode: PromptMode.Versioned, KeepOutputs: true, OutputSchema: { } schema }
            || PostExtractionDefinitions.OutputSchema(useCase.Key) is not { } expected)
            return false;
        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(schema), JsonNode.Parse(expected));
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

    private sealed record CallResult(string Status, string? Reason, AiRunModel? Run, ValidatedExtraction? Answer)
    {
        internal static CallResult Unavailable(string reason) => new("unavailable", reason, null, null);
    }
}
