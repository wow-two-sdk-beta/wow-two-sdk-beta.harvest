namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Declares a tenant's pull subscription to a pipeline.</summary>
public sealed record SubscriptionDeclaration
{
    /// <summary>Gets the subscribing tenant.</summary>
    public required Guid TenantId { get; init; }
    /// <summary>Gets the pool or tenant-owned container.</summary>
    public required Guid ContainerId { get; init; }
    /// <summary>Gets the pipeline to follow.</summary>
    public required Guid PipelineId { get; init; }
    /// <summary>Gets the opaque routing context echoed on delivery.</summary>
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();
    /// <summary>Gets the host-registered post filter to apply, or null to accept all posts.</summary>
    public string? FilterKey { get; init; }
    /// <summary>Gets the schema requested by the consumer.</summary>
    public string? Schema { get; init; }
    /// <summary>Gets which existing posts enter the new log.</summary>
    public BackfillMode Backfill { get; init; } = BackfillMode.Live;
    /// <summary>Gets the lower bound required by <see cref="BackfillMode.Since"/>.</summary>
    public DateTimeOffset? BackfillSinceUtc { get; init; }
}
