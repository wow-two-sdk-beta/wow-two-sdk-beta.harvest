namespace WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

/// <summary>Represents a registered tenant: who holds keys, containers and subscriptions.</summary>
public sealed record TenantModel
{
    /// <summary>Gets the tenant's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant's normalized handle, unique across the instance.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the tenant's display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets whether the tenant may use the service.</summary>
    public required TenantState State { get; init; }

    /// <summary>Gets whether the tenant may run on platform-owned provider credentials.</summary>
    public required bool PlatformCredentialsAllowed { get; init; }

    /// <summary>Gets when the tenant was registered (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }
}
