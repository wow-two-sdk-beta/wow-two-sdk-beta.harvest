namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Represents one tenant's interest — a channel, tags or keywords — with its own event log and delivery.</summary>
public sealed record SubscriptionModel
{
    /// <summary>Gets the subscription's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant that subscribed.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Gets what the subscription follows.</summary>
    public required SubscriptionKind Kind { get; init; }

    /// <summary>Gets the pipeline a channel subscription follows; null for tag and keyword subscriptions.</summary>
    public Guid? PipelineId { get; init; }

    /// <summary>Gets the tag keys or keyword sets a pool subscription matches.</summary>
    public IReadOnlyList<string> Rules { get; init; } = [];

    /// <summary>Gets how the log reaches the tenant.</summary>
    public required DeliveryMode Mode { get; init; }

    /// <summary>Gets the tenant's opaque routing data, echoed on every event, such as <c>{"catalog":"ocharo-fashion"}</c>.</summary>
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the host-registered post filter, or null when the subscription accepts all posts.</summary>
    public string? FilterKey { get; init; }

    /// <summary>Gets whether the subscription wants model extraction, billed to it.</summary>
    public bool WantsAi { get; init; }

    /// <summary>Gets the consumer schema extraction maps to, such as <c>wow2.event.v1</c>.</summary>
    public string? Schema { get; init; }

    /// <summary>Gets the longest the subscriber accepts posts going unchecked; the pipeline's interval follows the
    /// smallest.</summary>
    public TimeSpan? MaxStaleness { get; init; }

    /// <summary>Gets what the log started with.</summary>
    public BackfillMode Backfill { get; init; } = BackfillMode.Live;

    /// <summary>Gets the webhook endpoint for push delivery.</summary>
    public Uri? WebhookUrl { get; init; }

    /// <summary>Gets whether the subscription accumulates events.</summary>
    public required SubscriptionState State { get; init; }

    /// <summary>Gets when the tenant subscribed (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }
}
