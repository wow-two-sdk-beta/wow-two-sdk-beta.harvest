namespace WoW.Two.Sdk.Beta.Be.Harvest.Containers;

/// <summary>Represents a request to register a container — the pool, or a tenant's private container.</summary>
public sealed record ContainerRegistration
{
    /// <summary>Gets the container's visibility.</summary>
    public required ContainerKind Kind { get; init; }

    /// <summary>Gets the owning tenant: required for <see cref="ContainerKind.Private"/>, absent for the pool.</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Gets the tenant's own name for the container, such as an Events catalog slug; unique per tenant.</summary>
    public string? ExternalRef { get; init; }

    /// <summary>Gets where the container's data may live; unset until the personal-data read.</summary>
    public string? Residency { get; init; }

    /// <summary>Gets whether posts in the container may be embedded; off by default.</summary>
    public bool EmbeddingsEnabled { get; init; }
}
