using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Runs Layer 2 extraction: parses a tagged post's fields into one canonical shape per entity type through the
/// shared AI runner, keeps only evidence-backed values and stores each answer per post, content and use case. It never
/// writes the post; the entity layer reads the stored answers.</summary>
public interface IPostExtractionBroker
{
    /// <summary>Extracts one post now with the use case of its tagged entity type, whatever demand or the queue holds; a
    /// dry run calls the model and stores nothing.</summary>
    /// <param name="postId">The post.</param>
    /// <param name="dryRun">Whether to skip storing the answer.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The run; a failure when the post does not exist, its sources do not allow AI, its current title and text
    /// have no done tags naming an event or a listing, or no AI runner is composed.</returns>
    Task<Result<PostExtractionRunModel>> RunAsync(Guid postId, bool dryRun = false, CancellationToken cancellationToken = default);

    /// <summary>Queues stored posts whose current tags match a wanted pair and have no done answer: the backfill a new
    /// wanted pair needs.</summary>
    /// <param name="request">Which posts.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The number of extractions queued or re-armed.</returns>
    Task<Result<int>> BackfillAsync(PostExtractionBackfillRequest request, CancellationToken cancellationToken = default);

    /// <summary>Counts answers per use case by status and relation role, flags by proposed value, the queue, and each use
    /// case's cost and latency from the inference log; never post text.</summary>
    /// <param name="sinceUtc">The earliest answer to count; seven days back when omitted.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    Task<Result<PostExtractionReportModel>> ReportAsync(DateTimeOffset? sinceUtc = null, CancellationToken cancellationToken = default);

    /// <summary>Claims due queued extractions and runs each; the worker calls it on every tick.</summary>
    /// <param name="cancellationToken">The token that cancels the pass.</param>
    /// <returns>The number of extractions claimed.</returns>
    Task<int> ProcessDueAsync(CancellationToken cancellationToken = default);
}

/// <summary>Represents which stored posts a backfill queues.</summary>
public sealed record PostExtractionBackfillRequest
{
    /// <summary>Gets the earliest last change to include; every post when omitted.</summary>
    public DateTimeOffset? SinceUtc { get; init; }

    /// <summary>Gets the most extractions to queue, newest change first: 1 to 100,000.</summary>
    public int Limit { get; init; } = 1000;
}

/// <summary>Represents one extraction run of a post.</summary>
public sealed record PostExtractionRunModel
{
    /// <summary>Gets the post.</summary>
    public required Guid PostId { get; init; }

    /// <summary>Gets the post revision the answer was computed from.</summary>
    public required int Revision { get; init; }

    /// <summary>Gets the use case: <c>extract-event</c> or <c>extract-listing</c>.</summary>
    public required string UseCase { get; init; }

    /// <summary>Gets how the run ended: <c>done</c>, <c>failed</c> or <c>unavailable</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Gets why a run failed or was unavailable, as a short code such as <c>schema-invalid</c>.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets whether the answer was stored.</summary>
    public bool Stored { get; init; }

    /// <summary>Gets the deciding inference's id.</summary>
    public Guid? InferenceId { get; init; }

    /// <summary>Gets the model that answered, <c>{provider}/{model}</c>.</summary>
    public string? Model { get; init; }

    /// <summary>Gets the prompt version used.</summary>
    public int? PromptVersion { get; init; }

    /// <summary>Gets whether the answer came from the AI module's stored inferences.</summary>
    public bool FromCache { get; init; }

    /// <summary>Gets the run's cost in micro-USD over every attempt.</summary>
    public long CostMicroUsd { get; init; }

    /// <summary>Gets the post-to-entity role the answer chose.</summary>
    public string? Role { get; init; }

    /// <summary>Gets the validated answer, flags included.</summary>
    public JsonElement? Answer { get; init; }
}

/// <summary>Represents the extraction's counts since a time, per use case.</summary>
public sealed record PostExtractionReportModel
{
    /// <summary>Gets the earliest answer counted.</summary>
    public required DateTimeOffset SinceUtc { get; init; }

    /// <summary>Gets the counts of each use case, by key.</summary>
    public required IReadOnlyDictionary<string, PostExtractionUseCaseReport> UseCases { get; init; }
}

/// <summary>Represents one use case's counts, cost and latency.</summary>
public sealed record PostExtractionUseCaseReport
{
    /// <summary>Gets the answers by status.</summary>
    public required IReadOnlyDictionary<string, long> Statuses { get; init; }

    /// <summary>Gets the done answers by relation role.</summary>
    public required IReadOnlyDictionary<string, long> Roles { get; init; }

    /// <summary>Gets the flags of done answers by field and proposed value, most frequent first.</summary>
    public required IReadOnlyList<PostTagsFlagCount> Flags { get; init; }

    /// <summary>Gets the posts waiting in the queue.</summary>
    public long Queued { get; init; }

    /// <summary>Gets the model attempts the inference log holds for the use case since the report's start.</summary>
    public long? Attempts { get; init; }

    /// <summary>Gets their cost in micro-USD.</summary>
    public long? CostMicroUsd { get; init; }

    /// <summary>Gets the 95th percentile attempt latency in milliseconds, over the newest attempts sampled.</summary>
    public long? P95LatencyMs { get; init; }

    /// <summary>Gets how many attempts the latency percentile sampled, at most 1000.</summary>
    public int LatencySample { get; init; }
}
