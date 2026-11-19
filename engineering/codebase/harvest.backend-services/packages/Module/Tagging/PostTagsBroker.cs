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
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Tags posts through the shared AI runner and stores each answer per title-and-text hash.</summary>
internal sealed class PostTagsBroker(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource,
    ModulePostBroker posts,
    IEnumerable<IDeliveryPostFilter> filters,
    IOptions<PostTagsOptions> options,
    IServiceProvider services,
    TimeProvider time,
    ILogger<PostTagsBroker> logger) : IPostTagsBroker
{
    private const int MaxBackfill = 100_000;
    private const int MaxDisagreements = 1000;
    private const int MaxReviewTitle = 140;
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

    /// <inheritdoc />
    public async Task<Result<PostTagsRelevanceModel>> RelevanceAsync(PostTagsRelevanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = time.GetUtcNow();
        var (scope, refusal) = OpenRelevance(request, now);
        if (scope is null)
            return Result<PostTagsRelevanceModel>.Fail(refusal!);

        long posts = 0, undecided = 0, tagged = 0;
        long[] yes = new long[4], no = new long[4];
        await PostTagsStore.ReadRelevanceAsync(dataSource, scope.Since, row =>
        {
            posts++;
            if (row.EntityType is not null)
                tagged++;
            if (scope.Judge(row) is not (bool passed, int column))
            {
                undecided++;
                return;
            }
            (passed ? yes : no)[column]++;
        }, cancellationToken);
        var invalid = await PostTagsStore.CountInvalidAsync(dataSource, scope.Since, cancellationToken);
        var (attempts, cost, p95, sampled) = await InferenceStatsAsync(scope.Since, now, cancellationToken);
        return Result<PostTagsRelevanceModel>.Ok(new PostTagsRelevanceModel
        {
            SinceUtc = scope.Since, Rules = scope.Key, Wanted = scope.WantedLabels,
            Posts = posts, RulesYes = Counts(yes), RulesNo = Counts(no), RulesUndecided = undecided, InvalidAnswers = invalid,
            TaggedPosts = tagged, Attempts = attempts, CostMicroUsd = cost,
            CostPerPostMicroUsd = cost is { } spent && tagged > 0 ? (long)Math.Round((double)spent / tagged) : null,
            P95LatencyMs = p95, LatencySample = sampled,
        });

        static PostTagsRelevanceCounts Counts(long[] row) => new() { Match = row[0], NoMatch = row[1], Unsure = row[2], NotTagged = row[3] };
    }

    /// <inheritdoc />
    public async Task<Result<PostTagsDisagreementsModel>> DisagreementsAsync(PostTagsRelevanceRequest request, int limit = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (limit is < 1 or > MaxDisagreements)
            return Result<PostTagsDisagreementsModel>.Fail(AppErrorFactory.Validation($"The limit must be between 1 and {MaxDisagreements}."));
        var (scope, refusal) = OpenRelevance(request, time.GetUtcNow());
        if (scope is null)
            return Result<PostTagsDisagreementsModel>.Fail(refusal!);

        var items = new List<PostTagsDisagreement>();
        await PostTagsStore.ReadRelevanceAsync(dataSource, scope.Since, row =>
        {
            // Columns: 0 match, 1 no match, 2 unsure, 3 not tagged. Rules yes disagrees with a tag no match or unsure,
            // rules no with a tag match or unsure; an untagged post or one the filter could not judge is never listed.
            if (scope.Judge(row) is not (bool passed, int column) || column == 3 || (passed ? column == 0 : column == 1))
                return;
            var primary = row.Domains.FirstOrDefault(domain => domain.Primary);
            items.Add(new PostTagsDisagreement
            {
                PostId = row.PostId, Source = row.Source, Title = ReviewTitle(row.Post.Title), TitleEn = row.TitleEn,
                Rules = passed ? "yes" : "no", Tags = column switch { 0 => "match", 1 => "noMatch", _ => "unsure" },
                EntityType = row.EntityType!, PrimaryDomain = primary?.Node, Domains = [.. row.Domains.Select(domain => domain.Node)],
                Topics = row.Topics, About = row.About, Timeframe = row.Timeframe,
                Confidence = row.Confidence is { } confidence ? (double)confidence : null,
            });
        }, cancellationToken);
        return Result<PostTagsDisagreementsModel>.Ok(new PostTagsDisagreementsModel
        {
            Rules = scope.Key, Wanted = scope.WantedLabels,
            Items = [.. items.OrderBy(item => item.Rules == "yes" ? 0 : 1).ThenBy(item => item.PostId.ToString("D"), StringComparer.Ordinal).Take(limit)],
        });
    }

    /// <summary>Checks a relevance request once for both reports: the window, the pairs and the registered rules filter.</summary>
    private (RelevanceScope? Scope, AppError? Refusal) OpenRelevance(PostTagsRelevanceRequest request, DateTimeOffset now)
    {
        var since = (request.SinceUtc ?? now.AddDays(-7)).ToUniversalTime();
        if (since >= now)
            return (null, AppErrorFactory.Validation("The report must start before now."));
        var wanted = PostExtractionDemand.Normalize(request.Wanted ?? []);
        if (wanted.Count == 0)
            return (null, AppErrorFactory.Validation("Name at least one (entity type, domain) pair."));
        var rules = new DeliveryPostFilters(filters);
        if (string.IsNullOrWhiteSpace(request.RulesFilterKey) || !rules.Contains(request.RulesFilterKey))
            return (null, AppErrorFactory.Validation("The rules filter is not registered in this host."));
        return (new RelevanceScope(since, request.RulesFilterKey, wanted, rules), null);
    }

    /// <summary>Scrubs a post's title of contacts, then clips it for the operator review.</summary>
    private static string ReviewTitle(string? title)
    {
        string text = PostExtractionValidation.Scrub((title ?? "").Trim());
        return text.Length <= MaxReviewTitle ? text : text[..(char.IsHighSurrogate(text[MaxReviewTitle - 1]) ? MaxReviewTitle - 1 : MaxReviewTitle)];
    }

    /// <summary>Holds what both relevance reports compare a post by: the window, the rules filter and the wanted pairs.</summary>
    private sealed record RelevanceScope(DateTimeOffset Since, string Key, IReadOnlyList<PostExtractionWant> Wanted, DeliveryPostFilters Rules)
    {
        internal IReadOnlyList<string> WantedLabels => [.. Wanted.Select(want => $"{want.EntityType}:{want.Domain}")];

        /// <summary>Returns whether the rules pass the post and the column of the tags' verdict (0 match, 1 no match, 2 unsure,
        /// 3 no done answer); none when the filter could not judge the post.</summary>
        internal (bool Passed, int Column)? Judge(PostTagsRelevanceRow row)
        {
            if (Rules.Evaluate(Key, row.Post) is not { } passed)
                return null;
            int column = row.EntityType is null ? 3
                : PostTagsClassification.Verdict(row.EntityType, row.Domains, Wanted)!.Value.Verdict switch
                {
                    ClassificationVerdict.Relevant => 0,
                    ClassificationVerdict.Irrelevant => 1,
                    _ => 2,
                };
            return (passed, column);
        }
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
