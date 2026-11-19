using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Represents one event of a subscription's log: references to what changed, plus the payload rendered at send
/// or read time.</summary>
/// <remarks>Delivery is at least once with no global order; a consumer applies the highest <see cref="Revision"/> per
/// post and deduplicates on <see cref="EventId"/>.</remarks>
public sealed record DeliveryEvent
{
    /// <summary>Gets the event's identifier, stable across retries and redeliveries — the webhook id.</summary>
    public required Guid EventId { get; init; }

    /// <summary>Gets the content identity, equal across subscriptions and resyncs, such as a hash of post, revision and
    /// type. A selector change or an adopt writes it as the hash of post, revision, type and <c>:s{selectorVersion}</c>, so
    /// a second unmatch or re-match at the same revision stays a distinct item.</summary>
    public required string ItemId { get; init; }

    /// <summary>Gets the subscription whose log holds the event.</summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>Gets the event's type, one of <see cref="DeliveryEventTypes"/>.</summary>
    public required string Type { get; init; }

    /// <summary>Gets when the change happened (UTC).</summary>
    public required DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>Gets the pipeline the subscription follows; null for tag, keyword and selector subscriptions.</summary>
    public Guid? PipelineId { get; init; }

    /// <summary>Gets the post the event is about.</summary>
    public Guid? PostId { get; init; }

    /// <summary>Gets the post's revision, monotonic per post.</summary>
    public int? Revision { get; init; }

    /// <summary>Gets the link the event is about.</summary>
    public Guid? LinkId { get; init; }

    /// <summary>Gets the run the event is about.</summary>
    public Guid? RunId { get; init; }

    /// <summary>Gets why the event was written: <c>ingest</c>, <c>backfill</c> or <c>resync</c>.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the subscription's context, echoed.</summary>
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the selector's verdict on the revision, with the selector version and the clauses that decided it;
    /// null for a channel subscription.</summary>
    public SubscriptionMatch? Match { get; init; }

    /// <summary>Gets the payload rendered from the post revision: base fields, links with state, media and capture refs. A
    /// selector event also carries a top-level <c>context</c>: the subscription's context merged with the contexts of the
    /// include refs that matched the revision's context link.</summary>
    public JsonElement? Data { get; init; }
}
