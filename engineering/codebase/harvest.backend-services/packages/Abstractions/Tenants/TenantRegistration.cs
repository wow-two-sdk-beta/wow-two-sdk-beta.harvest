namespace WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

/// <summary>Represents a request to register a tenant — a consuming service such as Events or Haven.</summary>
public sealed record TenantRegistration
{
    /// <summary>Gets the tenant's stable handle: lowercase letters, digits and hyphens, trimmed and lowercased on save.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the tenant's display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets whether the tenant may run on platform-owned provider credentials; off until the credential owner is
    /// settled.</summary>
    public bool PlatformCredentialsAllowed { get; init; }
}
