using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;

/// <summary>Maps a row of <c>harvest_tenants</c>.</summary>
internal sealed class TenantRow
{
    public const string TableName = "harvest_tenants";

    public required Guid Id { get; set; }

    public required string Slug { get; set; }

    public required string Name { get; set; }

    public required TenantState State { get; set; }

    public required bool PlatformCredentialsAllowed { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
