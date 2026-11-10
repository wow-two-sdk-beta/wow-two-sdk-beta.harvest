namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents one registered source as the console's Sources area shows it: its state, last and next harvest,
/// new posts per day, sections and host health.</summary>
public sealed record SourceOverviewModel
{
    /// <summary>Gets the source's identifier.</summary>
    public required Guid SourceId { get; init; }

    /// <summary>Gets the display name, else the handle.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the kind of origin the source is.</summary>
    public required SourceFamily Family { get; init; }

    /// <summary>Gets the source's normalized identity.</summary>
    public required string Handle { get; init; }

    /// <summary>Gets the source's group; null when ungrouped.</summary>
    public Guid? GroupId { get; init; }

    /// <summary>Gets where the source's harvesting stands.</summary>
    public required SourceHarvestState State { get; init; }

    /// <summary>Gets the newest capture run across the source's sections.</summary>
    public SourceHarvestModel? LastHarvest { get; init; }

    /// <summary>Gets the earliest next capture among the harvesting sections (UTC).</summary>
    public DateTimeOffset? NextHarvestAt { get; init; }

    /// <summary>Gets the source's post links first seen on each of the last
    /// <see cref="SourceConstants.OverviewDays"/> UTC days, oldest first; the last entry is today.</summary>
    public required IReadOnlyList<int> NewPostsDaily { get; init; }

    /// <summary>Gets the source's sections, oldest first.</summary>
    public required IReadOnlyList<SourceSectionModel> Sections { get; init; }

    /// <summary>Gets the source's host health.</summary>
    public required SourceHealthModel Health { get; init; }
}
