namespace WoW.Two.Sdk.Beta.Be.Harvest.Entities;

/// <summary>Represents one occurrence on a calendar, with the event it belongs to.</summary>
public sealed record EntityCalendarEntryModel
{
    /// <summary>Gets the entity's id.</summary>
    public required Guid EntityId { get; init; }

    /// <summary>Gets its type.</summary>
    public required string EntityType { get; init; }

    /// <summary>Gets its canonical English name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets its primary taxonomy node.</summary>
    public string? PrimaryDomain { get; init; }

    /// <summary>Gets how often it runs.</summary>
    public required string Cadence { get; init; }

    /// <summary>Gets the occurrence; <see cref="OccurrenceModel.Predicted"/> marks one no source has announced yet.</summary>
    public required OccurrenceModel Occurrence { get; init; }

    /// <summary>Gets how many surviving posts are about the occurrence.</summary>
    public long Posts { get; init; }
}
