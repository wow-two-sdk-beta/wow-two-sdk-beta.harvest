namespace WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

/// <summary>Represents a request to issue an API key to a tenant.</summary>
public sealed record ApiKeyIssue
{
    /// <summary>Gets the name the tenant gives the key, such as the program that holds it.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the scopes the key grants, each one of <see cref="HarvestScopeConstants.TenantScopes"/>.</summary>
    public required IReadOnlyList<string> Scopes { get; init; }

    /// <summary>Gets when the key stops authenticating (UTC); never when null.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}
