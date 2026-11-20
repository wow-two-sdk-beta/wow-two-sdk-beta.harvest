namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;

/// <summary>Maps a row of <c>harvest_catalog_sources</c>: the instance's decision on one catalog entry (019).</summary>
internal sealed class CatalogRecordRow
{
    public const string TableName = "harvest_catalog_sources";

    public required string Key { get; set; }

    public required bool TurnedOn { get; set; }

    public required DateTimeOffset ChangedAt { get; set; }

    public required string ChangedBy { get; set; }
}
