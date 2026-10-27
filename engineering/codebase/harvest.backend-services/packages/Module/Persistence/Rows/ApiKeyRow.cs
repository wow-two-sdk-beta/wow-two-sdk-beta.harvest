namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;

/// <summary>Maps a row of <c>harvest_api_keys</c>: the hash and prefix of a secret, never the secret.</summary>
internal sealed class ApiKeyRow
{
    public const string TableName = "harvest_api_keys";

    public required Guid Id { get; set; }

    public required Guid TenantId { get; set; }

    public required string Name { get; set; }

    public required string Prefix { get; set; }

    public required string Hash { get; set; }

    public required List<string> Scopes { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? RotatedFrom { get; set; }
}
