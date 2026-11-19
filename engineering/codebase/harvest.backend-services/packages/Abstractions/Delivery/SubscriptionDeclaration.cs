namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Declares a tenant's pull subscription to a pipeline, or to a selector over one container.</summary>
public sealed record SubscriptionDeclaration
{
    /// <summary>Gets the subscribing tenant.</summary>
    public required Guid TenantId { get; init; }
    /// <summary>Gets the pool or tenant-owned container.</summary>
    public required Guid ContainerId { get; init; }
    /// <summary>Gets the pipeline a channel subscription follows; a channel needs it and a selector subscription refuses it.</summary>
    public Guid? PipelineId { get; init; }
    /// <summary>Gets the selector subscription's name, <c>^[a-z0-9][a-z0-9-]{0,63}$</c> and unique among the tenant's
    /// non-archived subscriptions; required with <see cref="Selector"/>.</summary>
    public string? Name { get; init; }
    /// <summary>Gets the selector to follow, which makes this a selector subscription; it refuses a pipeline and a filter.</summary>
    public SubscriptionSelector? Selector { get; init; }
    /// <summary>Gets the opaque routing context echoed on delivery.</summary>
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();
    /// <summary>Gets a host filter key or <c>tag:&lt;value&gt;</c>, or null to accept all posts.</summary>
    public string? FilterKey { get; init; }
    /// <summary>Gets the schema requested by the consumer.</summary>
    public string? Schema { get; init; }
    /// <summary>Gets which existing posts enter the new log.</summary>
    public BackfillMode Backfill { get; init; } = BackfillMode.Live;
    /// <summary>Gets the lower bound required by <see cref="BackfillMode.Since"/>.</summary>
    public DateTimeOffset? BackfillSinceUtc { get; init; }
}
