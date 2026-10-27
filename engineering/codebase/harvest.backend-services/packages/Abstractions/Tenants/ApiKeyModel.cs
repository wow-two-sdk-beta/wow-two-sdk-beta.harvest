namespace WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

/// <summary>Represents an issued API key as the store keeps it: the prefix and metadata, never the secret.</summary>
public sealed record ApiKeyModel
{
    /// <summary>Gets the key's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant the key acts for.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Gets the name the tenant gave the key.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the display prefix — the marker and the first random characters — to tell keys apart.</summary>
    public required string Prefix { get; init; }

    /// <summary>Gets the scopes the key grants.</summary>
    public required IReadOnlyList<string> Scopes { get; init; }

    /// <summary>Gets whether the key still authenticates.</summary>
    public required ApiKeyState State { get; init; }

    /// <summary>Gets when the key was issued (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Gets when the key stops authenticating (UTC); never when null.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; init; }

    /// <summary>Gets when the key last authenticated a request (UTC); null before its first use.</summary>
    public DateTimeOffset? LastUsedAtUtc { get; init; }

    /// <summary>Gets when the key was revoked (UTC); null while it is active.</summary>
    public DateTimeOffset? RevokedAtUtc { get; init; }
}
