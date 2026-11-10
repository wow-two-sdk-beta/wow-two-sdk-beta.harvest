namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;

/// <summary>Maps a row of <c>harvest_source_groups</c>.</summary>
internal sealed class SourceGroupRow
{
    public const string TableName = "harvest_source_groups";

    public required Guid Id { get; set; }

    public required Guid ContainerId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
