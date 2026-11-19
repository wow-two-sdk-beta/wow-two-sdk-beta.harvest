namespace WoW.Two.Sdk.Beta.Be.Harvest.Entities;

/// <summary>Represents one run of an event: an edition with its dates, status and place.</summary>
public sealed record OccurrenceModel
{
    /// <summary>Gets the occurrence's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets its label: the edition label a source gave, else the year it starts.</summary>
    public required string Label { get; init; }

    /// <summary>Gets its first day; the first day of the month or year at a coarser precision.</summary>
    public required DateOnly Starts { get; init; }

    /// <summary>Gets its last day, when stated.</summary>
    public DateOnly? Ends { get; init; }

    /// <summary>Gets the finest unit its dates state: <c>year</c>, <c>month</c>, <c>day</c> or finer.</summary>
    public required string Precision { get; init; }

    /// <summary>Gets whether a source <c>stated</c> its dates or they are <c>estimated</c>.</summary>
    public required string Certainty { get; init; }

    /// <summary>Gets whether the cadence predicted it and no source has announced it yet.</summary>
    public bool Predicted { get; init; }

    /// <summary>Gets its status: <c>scheduled</c>, <c>cancelled</c>, <c>postponed</c>, <c>held</c> or <c>unknown</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Gets where it takes place, when known.</summary>
    public OccurrencePlaceModel? Place { get; init; }
}
