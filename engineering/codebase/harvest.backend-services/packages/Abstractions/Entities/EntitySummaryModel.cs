namespace WoW.Two.Sdk.Beta.Be.Harvest.Entities;

/// <summary>Represents one entity a search finds: who it is, the years it has something for and its counts.</summary>
public sealed record EntitySummaryModel
{
    /// <summary>Gets the entity's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the container it belongs to.</summary>
    public required Guid ContainerId { get; init; }

    /// <summary>Gets its type, one of <see cref="EntityTypes"/>.</summary>
    public required string EntityType { get; init; }

    /// <summary>Gets its canonical English name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the other names sources gave it, in their languages.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Gets its primary taxonomy node, such as <c>style-fashion/womens-fashion</c>.</summary>
    public string? PrimaryDomain { get; init; }

    /// <summary>Gets its status: <c>active</c>, or <c>sold</c> or <c>withdrawn</c> for a listing.</summary>
    public required string Status { get; init; }

    /// <summary>Gets how often an event runs: <c>annual</c>, <c>semiannual</c>, <c>quarterly</c>, <c>monthly</c>,
    /// <c>irregular</c> or <c>unknown</c>.</summary>
    public required string Cadence { get; init; }

    /// <summary>Gets how similar its name or an alias is to the query, 0 to 1; null when the search had no query.</summary>
    public decimal? Score { get; init; }

    /// <summary>Gets the years it has an occurrence or a bound post in, earliest first, predicted ones marked.</summary>
    public required IReadOnlyList<EntityYearSummaryModel> Years { get; init; }

    /// <summary>Gets the predicted next start, when the cadence predicts one.</summary>
    public DateOnly? Predicted { get; init; }

    /// <summary>Gets how many real occurrences it has.</summary>
    public long Occurrences { get; init; }

    /// <summary>Gets how many surviving posts bind to it.</summary>
    public long Posts { get; init; }

    /// <summary>Gets how many sources published those posts.</summary>
    public long Sources { get; init; }

    /// <summary>Gets when a post last bound to it.</summary>
    public required DateTimeOffset LastSeenAtUtc { get; init; }
}
