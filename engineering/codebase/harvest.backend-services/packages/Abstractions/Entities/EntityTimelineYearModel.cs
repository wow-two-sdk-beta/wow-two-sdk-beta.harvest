namespace WoW.Two.Sdk.Beta.Be.Harvest.Entities;

/// <summary>Represents one year of an entity's timeline.</summary>
public sealed record EntityTimelineYearModel
{
    /// <summary>Gets the year.</summary>
    public required int Year { get; init; }

    /// <summary>Gets the occurrences starting that year, earliest first; a predicted one is marked.</summary>
    public required IReadOnlyList<OccurrenceModel> Occurrences { get; init; }

    /// <summary>Gets how many surviving posts are about that year (<see cref="EntityPostModel.Year"/>).</summary>
    public long Posts { get; init; }
}
