using WoW.Two.Sdk.Beta.Be.Harvest.Containers;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;

/// <summary>Maps a row of <c>harvest_containers</c>.</summary>
internal sealed class ContainerRow
{
    public const string TableName = "harvest_containers";

    public required Guid Id { get; set; }

    public required ContainerKind Kind { get; set; }

    public Guid? TenantId { get; set; }

    public string? ExternalRef { get; set; }

    public string? Residency { get; set; }

    public required bool EmbeddingsEnabled { get; set; }

    public required ContainerState State { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
