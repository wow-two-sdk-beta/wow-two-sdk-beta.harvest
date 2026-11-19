using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Runs the Layer 1 <c>post-tags</c> pass: tags a post through the shared AI runner, stores the answer per
/// title-and-text hash and writes done tags back to the post as a new revision.</summary>
public interface IPostTagsBroker
{
    /// <summary>Tags one post now, whatever the queue holds; a dry run calls the model and writes nothing.</summary>
    /// <param name="postId">The post.</param>
    /// <param name="dryRun">Whether to skip storing the answer and the write-back.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The run; a failure when the post does not exist, its sources do not allow AI, or no AI runner is composed.</returns>
    Task<Result<PostTagsRunModel>> RunAsync(Guid postId, bool dryRun = false, CancellationToken cancellationToken = default);

    /// <summary>Queues stored posts whose current title and text have no done answer.</summary>
    /// <param name="request">Which posts.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The number of posts queued or re-armed.</returns>
    Task<Result<int>> BackfillAsync(PostTagsBackfillRequest request, CancellationToken cancellationToken = default);

    /// <summary>Counts answers by status, entity type and domain, flags by proposed value, and the use case's cost and
    /// latency from the inference log; never post text.</summary>
    /// <param name="sinceUtc">The earliest answer to count; seven days back when omitted.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    Task<Result<PostTagsReportModel>> ReportAsync(DateTimeOffset? sinceUtc = null, CancellationToken cancellationToken = default);

    /// <summary>Compares a rules filter's verdict with the tags' verdict for the wanted pairs over stored posts, as counts
    /// only: the shadow report a switch from rules to tags is judged by (events-integration S15).</summary>
    /// <param name="request">The rules filter, the pairs and the window.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The counts, invalid answers, and the use case's cost per tagged post and p95 latency from the inference
    /// log; a failure when the filter is not registered or no pair is given.</returns>
    Task<Result<PostTagsRelevanceModel>> RelevanceAsync(PostTagsRelevanceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists the posts the rules filter and the tags disagree on, for an operator's review: the rules pass a post
    /// whose tags match no pair or are unsure, or the rules reject one whose tags match or are unsure. The same posts,
    /// rules evaluation and tag lookup as <see cref="RelevanceAsync"/>; a post with no done answer, or one the filter could
    /// not judge, is never listed.</summary>
    /// <param name="request">The rules filter, the pairs and the window.</param>
    /// <param name="limit">The most posts to list: 1 to 1,000; posts the rules pass come first, then by post id.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The posts; a failure when the filter is not registered, no pair is given, or the limit is out of range.</returns>
    /// <remarks>Unlike every other report, each item carries the post's title, with phone numbers, emails and handles
    /// scrubbed and clipped to 140 characters: an operator review, never a subscriber's feed.</remarks>
    Task<Result<PostTagsDisagreementsModel>> DisagreementsAsync(PostTagsRelevanceRequest request, int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>Claims due queued posts and tags each; the worker calls it on every tick.</summary>
    /// <param name="cancellationToken">The token that cancels the pass.</param>
    /// <returns>The number of posts claimed.</returns>
    Task<int> ProcessDueAsync(CancellationToken cancellationToken = default);
}

/// <summary>Represents which stored posts a backfill queues.</summary>
public sealed record PostTagsBackfillRequest
{
    /// <summary>Gets the earliest last change to include; every post when omitted.</summary>
    public DateTimeOffset? SinceUtc { get; init; }

    /// <summary>Gets the most posts to queue, newest change first: 1 to 100,000.</summary>
    public int Limit { get; init; } = 1000;

    /// <summary>Gets a source the posts must have a link from.</summary>
    public Guid? SourceId { get; init; }
}

/// <summary>Represents one tagging run of a post.</summary>
public sealed record PostTagsRunModel
{
    /// <summary>Gets the post.</summary>
    public required Guid PostId { get; init; }

    /// <summary>Gets the post revision the answer was computed from.</summary>
    public required int Revision { get; init; }

    /// <summary>Gets how the run ended: <c>done</c>, <c>failed</c> or <c>unavailable</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Gets why a run failed or was unavailable, as a short code such as <c>schema-invalid</c>.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets whether the answer was stored.</summary>
    public bool Stored { get; init; }

    /// <summary>Gets the revision the write-back created, when the tags changed the post.</summary>
    public int? NewRevision { get; init; }

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

    /// <summary>Gets the validated answer: the stored fields and flags.</summary>
    public JsonElement? Answer { get; init; }
}

/// <summary>Represents the pass's counts since a time, with the use case's cost and latency.</summary>
public sealed record PostTagsReportModel
{
    /// <summary>Gets the earliest answer counted.</summary>
    public required DateTimeOffset SinceUtc { get; init; }

    /// <summary>Gets the answers by status.</summary>
    public required IReadOnlyDictionary<string, long> Statuses { get; init; }

    /// <summary>Gets the done answers by entity type.</summary>
    public required IReadOnlyDictionary<string, long> EntityTypes { get; init; }

    /// <summary>Gets the done answers by their primary node's domain.</summary>
    public required IReadOnlyDictionary<string, long> Domains { get; init; }

    /// <summary>Gets the flags of done answers by field and proposed value, most frequent first, at most 50.</summary>
    public required IReadOnlyList<PostTagsFlagCount> Flags { get; init; }

    /// <summary>Gets the posts waiting in the queue.</summary>
    public long Queued { get; init; }

    /// <summary>Gets the model attempts the inference log holds for the use case since <see cref="SinceUtc"/>.</summary>
    public long? Attempts { get; init; }

    /// <summary>Gets their cost in micro-USD.</summary>
    public long? CostMicroUsd { get; init; }

    /// <summary>Gets the 95th percentile attempt latency in milliseconds, over the newest attempts sampled.</summary>
    public long? P95LatencyMs { get; init; }

    /// <summary>Gets how many attempts the latency percentile sampled, at most 1000.</summary>
    public int LatencySample { get; init; }
}

/// <summary>Represents how often a field was flagged with one proposed value.</summary>
public sealed record PostTagsFlagCount
{
    /// <summary>Gets the flagged field, such as <c>domains</c> or <c>entityType</c>.</summary>
    public required string Field { get; init; }

    /// <summary>Gets the value the model proposed; none when it proposed nothing.</summary>
    public string? Proposed { get; init; }

    /// <summary>Gets the number of flags.</summary>
    public required long Count { get; init; }
}

/// <summary>Represents which rules, pairs and window a relevance report compares.</summary>
public sealed record PostTagsRelevanceRequest
{
    /// <summary>Gets the key of the registered delivery filter whose verdict is the rules side, such as
    /// <c>ocharo-fashion-v1</c>.</summary>
    public required string RulesFilterKey { get; init; }

    /// <summary>Gets the (entity type, domain) pairs a tag verdict matches, such as <c>event</c> + <c>style-fashion</c>.</summary>
    public required IReadOnlyList<PostExtractionWant> Wanted { get; init; }

    /// <summary>Gets the earliest last change of a post to count; seven days back when omitted.</summary>
    public DateTimeOffset? SinceUtc { get; init; }
}

/// <summary>Represents the shadow comparison of a rules filter with the tags' verdict: counts only, never post text.</summary>
/// <remarks>The posts are current, their sources allow the model to read them, and they changed since
/// <see cref="SinceUtc"/>.</remarks>
public sealed record PostTagsRelevanceModel
{
    /// <summary>Gets the earliest last change counted.</summary>
    public required DateTimeOffset SinceUtc { get; init; }

    /// <summary>Gets the rules filter's key.</summary>
    public required string Rules { get; init; }

    /// <summary>Gets the pairs, each <c>{entityType}:{domain}</c>.</summary>
    public required IReadOnlyList<string> Wanted { get; init; }

    /// <summary>Gets the posts counted.</summary>
    public required long Posts { get; init; }

    /// <summary>Gets the posts the rules pass, by tag verdict.</summary>
    public required PostTagsRelevanceCounts RulesYes { get; init; }

    /// <summary>Gets the posts the rules reject, by tag verdict.</summary>
    public required PostTagsRelevanceCounts RulesNo { get; init; }

    /// <summary>Gets the posts the rules filter could not judge because it threw; they are in neither row.</summary>
    public long RulesUndecided { get; init; }

    /// <summary>Gets the answers stored since <see cref="SinceUtc"/> in a shape the pass refused (<c>schema-invalid</c>,
    /// <c>answer-invalid</c>) that no done answer has replaced.</summary>
    public long InvalidAnswers { get; init; }

    /// <summary>Gets the counted posts whose current title and text have a done answer.</summary>
    public long TaggedPosts { get; init; }

    /// <summary>Gets the model attempts the inference log holds for <c>post-tags</c> since <see cref="SinceUtc"/>.</summary>
    public long? Attempts { get; init; }

    /// <summary>Gets their cost in micro-USD.</summary>
    public long? CostMicroUsd { get; init; }

    /// <summary>Gets <see cref="CostMicroUsd"/> per tagged post, rounded; none without a tagged post.</summary>
    public long? CostPerPostMicroUsd { get; init; }

    /// <summary>Gets the 95th percentile attempt latency in milliseconds, over the newest attempts sampled.</summary>
    public long? P95LatencyMs { get; init; }

    /// <summary>Gets how many attempts the latency percentile sampled, at most 1000.</summary>
    public int LatencySample { get; init; }
}

/// <summary>Represents one rules side of the comparison, split by the tags' verdict.</summary>
public sealed record PostTagsRelevanceCounts
{
    /// <summary>Gets the posts whose tags match a pair: a relevant verdict.</summary>
    public long Match { get; init; }

    /// <summary>Gets the posts whose tags match no pair: an irrelevant verdict.</summary>
    public long NoMatch { get; init; }

    /// <summary>Gets the posts whose entity type or primary domain is unsure: an uncertain verdict.</summary>
    public long Unsure { get; init; }

    /// <summary>Gets the posts whose current title and text have no done answer yet.</summary>
    public long NotTagged { get; init; }
}

/// <summary>Represents the posts a rules filter and the tags disagree on: an operator review that carries titles.</summary>
public sealed record PostTagsDisagreementsModel
{
    /// <summary>Gets the rules filter's key.</summary>
    public required string Rules { get; init; }

    /// <summary>Gets the pairs, each <c>{entityType}:{domain}</c>.</summary>
    public required IReadOnlyList<string> Wanted { get; init; }

    /// <summary>Gets the posts, rules <c>yes</c> first, then by post id, at most the requested limit.</summary>
    public required IReadOnlyList<PostTagsDisagreement> Items { get; init; }
}

/// <summary>Represents one post whose rules verdict and tags verdict disagree.</summary>
public sealed record PostTagsDisagreement
{
    /// <summary>Gets the post.</summary>
    public required Guid PostId { get; init; }

    /// <summary>Gets the label of the source supplying the post's title, else its text: <c>{family}/{handle}</c>.</summary>
    public string? Source { get; init; }

    /// <summary>Gets the post's title with phone numbers, emails and handles scrubbed, clipped to 140 characters.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the tag answer's English title.</summary>
    public string? TitleEn { get; init; }

    /// <summary>Gets the rules verdict: <c>yes</c> or <c>no</c>.</summary>
    public required string Rules { get; init; }

    /// <summary>Gets the tags' verdict for the wanted pairs: <c>match</c>, <c>noMatch</c> or <c>unsure</c>.</summary>
    public required string Tags { get; init; }

    /// <summary>Gets the answer's entity type.</summary>
    public required string EntityType { get; init; }

    /// <summary>Gets the key of the answer's primary taxonomy node; none when the answer kept no known node.</summary>
    public string? PrimaryDomain { get; init; }

    /// <summary>Gets the keys of every taxonomy node the answer kept.</summary>
    public required IReadOnlyList<string> Domains { get; init; }

    /// <summary>Gets the answer's English topic slugs.</summary>
    public required IReadOnlyList<string> Topics { get; init; }

    /// <summary>Gets the answer's one-line description of what the post is about.</summary>
    public string? About { get; init; }

    /// <summary>Gets the answer's timeframe: <c>upcoming</c>, <c>ongoing</c>, <c>past</c>, <c>evergreen</c> or <c>unsure</c>.</summary>
    public string? Timeframe { get; init; }

    /// <summary>Gets the answer's confidence, 0 to 1.</summary>
    public double? Confidence { get; init; }
}
