namespace WoW.Two.Sdk.Beta.Be.Harvest.Containers;

/// <summary>Represents a registered container: the visibility boundary every source, pipeline and post lives in.</summary>
public sealed record ContainerModel
{
    /// <summary>Gets the container's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the container's visibility.</summary>
    public required ContainerKind Kind { get; init; }

    /// <summary>Gets the owning tenant; null for the pool.</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Gets the tenant's own name for the container.</summary>
    public string? ExternalRef { get; init; }

    /// <summary>Gets where the container's data may live.</summary>
    public string? Residency { get; init; }

    /// <summary>Gets whether posts in the container may be embedded.</summary>
    public required bool EmbeddingsEnabled { get; init; }

    /// <summary>Gets whether the container is in use.</summary>
    public required ContainerState State { get; init; }

    /// <summary>Gets when the container was registered (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }
}
