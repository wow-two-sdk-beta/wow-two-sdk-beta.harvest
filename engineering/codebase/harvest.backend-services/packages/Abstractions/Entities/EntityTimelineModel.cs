namespace WoW.Two.Sdk.Beta.Be.Harvest.Entities;

/// <summary>Represents an entity with its occurrences by year: the history of an event and its predicted next run.</summary>
public sealed record EntityTimelineModel
{
    /// <summary>Gets the entity's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the entity asked for when it was merged into this one.</summary>
    public Guid? RequestedId { get; init; }

    /// <summary>Gets the container it belongs to.</summary>
    public required Guid ContainerId { get; init; }

    /// <summary>Gets its type, one of <see cref="EntityTypes"/>.</summary>
    public required string EntityType { get; init; }

    /// <summary>Gets its canonical English name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the other names sources gave it, in their languages.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Gets its primary taxonomy node.</summary>
    public string? PrimaryDomain { get; init; }

    /// <summary>Gets its status: <c>active</c>, or <c>sold</c> or <c>withdrawn</c> for a listing.</summary>
    public required string Status { get; init; }

    /// <summary>Gets how often an event runs.</summary>
    public required string Cadence { get; init; }

    /// <summary>Gets how sure the cadence is, 0 to 1.</summary>
    public decimal CadenceConfidence { get; init; }

    /// <summary>Gets when a post first bound to it.</summary>
    public required DateTimeOffset FirstSeenAtUtc { get; init; }

    /// <summary>Gets when a post last bound to it.</summary>
    public required DateTimeOffset LastSeenAtUtc { get; init; }

    /// <summary>Gets how many surviving posts bind to it.</summary>
    public long Posts { get; init; }

    /// <summary>Gets how many sources published those posts.</summary>
    public long Sources { get; init; }

    /// <summary>Gets the years it has an occurrence or a bound post in, earliest first.</summary>
    public required IReadOnlyList<EntityTimelineYearModel> Years { get; init; }
}
