using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Reprocessing;

/// <summary>Gives operators control over the AI passes (<c>post-tags</c>, <c>extract-event</c>, <c>extract-listing</c>): re-runs
/// over named posts, listed sources or every source, their progress, and eval sets built from stored posts.</summary>
/// <remarks>Re-runs queue through the passes' own work queue, so the workers, the source modes and the retries apply as to
/// any queued post; nothing here calls a model.</remarks>
public interface IPostReprocessingBroker
{
    /// <summary>Queues a re-run, or counts it and estimates its cost without queueing anything (a dry run).</summary>
    /// <param name="request">The scope, the filters and the use cases.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The run with its counts per use case; a failure for a malformed request or a host without the AI passes.</returns>
    Task<Result<PostRerunModel>> RerunAsync(PostRerunRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads a re-run with the counts it queued and what of it the queues still hold.</summary>
    /// <param name="runId">The run.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The run's progress; not found for an unknown run.</returns>
    Task<Result<PostRerunStatusModel>> GetRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Adds a seeded sample of stored posts to an eval set of the AI module, creating the set when it is missing;
    /// each item's input resolves from its <c>harvest:post:{id}@{revision}</c> subject, its expected answer starts empty
    /// or as the current done answer, marked unreviewed.</summary>
    /// <param name="request">The set, the use case, the sources and the sample.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What was added; a failure for a malformed request, a set of another use case, or a host without evals.</returns>
    Task<Result<PostEvalSetModel>> BuildEvalSetAsync(PostEvalSetRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Represents a re-run: one scope (named posts, listed sources or every source), optional filters and the use cases.</summary>
public sealed record PostRerunRequest
{
    /// <summary>Gets the use cases to re-run; none re-runs all three. Extraction for a post the same run re-tags follows its
    /// tags rather than running beside them.</summary>
    public IReadOnlyList<string> UseCases { get; init; } = [];

    /// <summary>Gets the posts to re-run, at most 1,000: an explicit scope, which also runs posts of a <c>manual</c> source and
    /// extracts whatever the demand.</summary>
    public IReadOnlyList<Guid> PostIds { get; init; } = [];

    /// <summary>Gets the sources whose posts to re-run, at most 100: an explicit scope, which also runs a <c>manual</c>
    /// source's posts.</summary>
    public IReadOnlyList<Guid> SourceIds { get; init; } = [];

    /// <summary>Gets whether to re-run the posts of every source; a <c>manual</c> source's posts are left out.</summary>
    public bool AllSources { get; init; }

    /// <summary>Gets the earliest last change of a post to include; every post when omitted.</summary>
    public DateTimeOffset? SinceUtc { get; init; }

    /// <summary>Gets a prompt version: only posts whose current done answer came from an older version re-run.</summary>
    public int? PromptBefore { get; init; }

    /// <summary>Gets whether to count and estimate only, queueing nothing.</summary>
    public bool DryRun { get; init; }

    /// <summary>Gets who asked for the run, kept on its record.</summary>
    public string? RequestedBy { get; init; }
}

/// <summary>Represents a queued re-run, or a dry run's counts.</summary>
public sealed record PostRerunModel
{
    /// <summary>Gets the run, which the status route reads; none for a dry run.</summary>
    public Guid? RunId { get; init; }

    /// <summary>Gets whether nothing was queued.</summary>
    public required bool DryRun { get; init; }

    /// <summary>Gets the scope: <c>posts</c>, <c>sources</c> or <c>all</c>.</summary>
    public required string Scope { get; init; }

    /// <summary>Gets the use cases re-run.</summary>
    public required IReadOnlyList<string> UseCases { get; init; }

    /// <summary>Gets the posts queued over every use case; for a dry run, the posts that would be.</summary>
    public long Queued { get; init; }

    /// <summary>Gets the posts skipped over every use case: a <c>manual</c> source in a run over every source, a row a worker
    /// holds, or a named post the use case cannot take.</summary>
    public long Skipped { get; init; }

    /// <summary>Gets the posts refused over every use case because a source supplying their title or text is <c>off</c>.</summary>
    public long Refused { get; init; }

    /// <summary>Gets the estimated cost in micro-USD of the queued posts, over the use cases with a cost history.</summary>
    public long? EstimatedCostMicroUsd { get; init; }

    /// <summary>Gets the counts and estimate of each use case, by key.</summary>
    public required IReadOnlyDictionary<string, PostRerunCounts> ByUseCase { get; init; }

    /// <summary>Gets when the run was asked for (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

/// <summary>Represents one use case's share of a re-run.</summary>
public sealed record PostRerunCounts
{
    /// <summary>Gets the posts queued; for a dry run, the posts that would be.</summary>
    public long Queued { get; init; }

    /// <summary>Gets the posts skipped: <see cref="Manual"/>, <see cref="InFlight"/> and <see cref="NotEligible"/> together.</summary>
    public long Skipped { get; init; }

    /// <summary>Gets the posts of a <c>manual</c> source left out of a run over every source; named posts and listed sources
    /// run them.</summary>
    public long Manual { get; init; }

    /// <summary>Gets the posts a worker holds right now.</summary>
    public long InFlight { get; init; }

    /// <summary>Gets the named posts the use case cannot take: missing, merged, purged, empty, or (extraction) without done
    /// tags of the use case's entity type.</summary>
    public long NotEligible { get; init; }

    /// <summary>Gets the posts refused because a source supplying their title or text is <c>off</c>.</summary>
    public long Refused { get; init; }

    /// <summary>Gets the model route the estimate prices: the first configured route of the use case.</summary>
    public string? Model { get; init; }

    /// <summary>Gets the recent cost per post of the use case on <see cref="Model"/> from the inference log, in micro-USD;
    /// none without history.</summary>
    public long? CostPerPostMicroUsd { get; init; }

    /// <summary>Gets how many posts the cost per post averages, from the newest production calls of the last 30 days.</summary>
    public int CostBasis { get; init; }

    /// <summary>Gets <see cref="Queued"/> × <see cref="CostPerPostMicroUsd"/>: an upper bound, as a cached answer costs
    /// nothing.</summary>
    public long? EstimatedCostMicroUsd { get; init; }
}

/// <summary>Represents a re-run's record and what of it the queues still hold.</summary>
public sealed record PostRerunStatusModel
{
    /// <summary>Gets the run.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the scope: <c>posts</c>, <c>sources</c> or <c>all</c>.</summary>
    public required string Scope { get; init; }

    /// <summary>Gets the use cases re-run.</summary>
    public required IReadOnlyList<string> UseCases { get; init; }

    /// <summary>Gets the prompt version filter, when one was given.</summary>
    public int? PromptBefore { get; init; }

    /// <summary>Gets the since filter, when one was given.</summary>
    public DateTimeOffset? SinceUtc { get; init; }

    /// <summary>Gets who asked for the run.</summary>
    public string? RequestedBy { get; init; }

    /// <summary>Gets when the run was asked for (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Gets the counts the run queued with, by use case.</summary>
    public required IReadOnlyDictionary<string, PostRerunCounts> Requested { get; init; }

    /// <summary>Gets the queue rows the run still has, by work kind — its own use cases and the extraction and entity
    /// resolution that followed them.</summary>
    public required IReadOnlyDictionary<string, PostRerunProgress> Remaining { get; init; }

    /// <summary>Gets whether the queues hold nothing of the run any more.</summary>
    public bool Done { get; init; }
}

/// <summary>Represents one work kind's rows of a run still in the queue.</summary>
public sealed record PostRerunProgress
{
    /// <summary>Gets the rows waiting for a worker, never tried or due again.</summary>
    public long Waiting { get; init; }

    /// <summary>Gets the rows a worker holds now.</summary>
    public long Running { get; init; }

    /// <summary>Gets the rows whose last attempt failed and that retry after their delay.</summary>
    public long Retrying { get; init; }

    /// <summary>Gets the most frequent last errors of the retrying rows, by code.</summary>
    public IReadOnlyDictionary<string, long> Errors { get; init; } = new Dictionary<string, long>();
}

/// <summary>Represents an eval set to fill from stored posts.</summary>
public sealed record PostEvalSetRequest
{
    /// <summary>Gets the set key: lowercase letters, digits and hyphens; created when missing.</summary>
    public required string Set { get; init; }

    /// <summary>Gets the use case the set evaluates: <c>post-tags</c>, <c>extract-event</c> or <c>extract-listing</c>.</summary>
    public required string UseCase { get; init; }

    /// <summary>Gets the sources to sample from; every source when none.</summary>
    public IReadOnlyList<Guid> SourceIds { get; init; } = [];

    /// <summary>Gets how many posts to sample, 1 to 500.</summary>
    public int Sample { get; init; } = 50;

    /// <summary>Gets the sampling seed: the same seed over the same posts picks the same sample.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>Gets where expected answers start: <c>empty</c> (an empty object) or <c>current</c> (the post's current done
    /// answer, which then must exist). Both are tagged <c>unreviewed</c>.</summary>
    public string Expected { get; init; } = PostReprocessingConstants.ExpectedEmpty;

    /// <summary>Gets the earliest last change of a post to sample; every post when omitted.</summary>
    public DateTimeOffset? SinceUtc { get; init; }

    /// <summary>Gets the answer fields a new set scores; none compares every field the expected answer names.</summary>
    public IReadOnlyList<string> ScoreFields { get; init; } = [];

    /// <summary>Gets a note a new set keeps.</summary>
    public string? Note { get; init; }
}

/// <summary>Represents what filling an eval set did.</summary>
public sealed record PostEvalSetModel
{
    /// <summary>Gets the set key.</summary>
    public required string Set { get; init; }

    /// <summary>Gets the use case the set evaluates.</summary>
    public required string UseCase { get; init; }

    /// <summary>Gets whether the set was created by this call.</summary>
    public bool Created { get; init; }

    /// <summary>Gets the posts the sample drew.</summary>
    public int Sampled { get; init; }

    /// <summary>Gets the items added.</summary>
    public int Added { get; init; }

    /// <summary>Gets the sampled posts already in the set, or whose subject did not resolve.</summary>
    public int Skipped { get; init; }

    /// <summary>Gets the added items' ids.</summary>
    public required IReadOnlyList<Guid> ItemIds { get; init; }
}
