namespace WoW.Two.Sdk.Beta.Be.Harvest.Entities;

/// <summary>Represents one year an entity has something for: the occurrences starting in it and the posts about it.</summary>
public sealed record EntityYearSummaryModel
{
    /// <summary>Gets the year.</summary>
    public required int Year { get; init; }

    /// <summary>Gets how many real occurrences start that year.</summary>
    public long Occurrences { get; init; }

    /// <summary>Gets whether the year holds the predicted next occurrence.</summary>
    public bool Predicted { get; init; }

    /// <summary>Gets how many surviving posts are about that year (<see cref="EntityPostModel.Year"/>).</summary>
    public long Posts { get; init; }
}
