namespace WoW.Two.Sdk.Beta.Be.Harvest.Entities;

/// <summary>Represents one change to an entity in its container's change feed: what changed, the entity as it then was
/// and, for an occurrence, its state after and before.</summary>
/// <remarks>The feed is at least once with no global order beyond the log's; a consumer deduplicates on
/// <see cref="EventId"/> and, to rebuild an entity, reads it again with <c>GetAsync</c>. It never carries post text.</remarks>
public sealed record EntityEvent
{
    /// <summary>Gets the event's id, stable across replays.</summary>
    public required Guid EventId { get; init; }

    /// <summary>Gets the event's type, one of <see cref="EntityEventTypes"/>.</summary>
    public required string Type { get; init; }

    /// <summary>Gets when the change happened (UTC).</summary>
    public required DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>Gets the container whose feed holds the event.</summary>
    public required Guid ContainerId { get; init; }

    /// <summary>Gets the entity that changed.</summary>
    public required Guid EntityId { get; init; }

    /// <summary>Gets its type, one of <see cref="EntityTypes"/>.</summary>
    public required string EntityType { get; init; }

    /// <summary>Gets its canonical name at the time.</summary>
    public required string Name { get; init; }

    /// <summary>Gets its primary taxonomy node at the time.</summary>
    public string? PrimaryDomain { get; init; }

    /// <summary>Gets its status at the time.</summary>
    public required string Status { get; init; }

    /// <summary>Gets its cadence at the time.</summary>
    public required string Cadence { get; init; }

    /// <summary>Gets the occurrence an <c>occurrence.*</c> event is about.</summary>
    public Guid? OccurrenceId { get; init; }

    /// <summary>Gets the occurrence's state after the change; for <c>occurrence.removed</c>, its last state.</summary>
    public OccurrenceModel? Occurrence { get; init; }

    /// <summary>Gets the occurrence's state before an <c>occurrence.changed</c> or <c>occurrence.confirmed</c>.</summary>
    public OccurrenceModel? Previous { get; init; }

    /// <summary>Gets what changed: <c>dates</c>, <c>status</c> or <c>place</c> of an occurrence; <c>status</c>,
    /// <c>cadence</c> or <c>price</c> of an entity.</summary>
    public IReadOnlyList<string> Changes { get; init; } = [];

    /// <summary>Gets the entity a merge folded this one into, or the one a split keeps it apart from.</summary>
    public Guid? RelatedEntityId { get; init; }

    /// <summary>Gets the predicted occurrence an <c>occurrence.predicted</c> replaces.</summary>
    public Guid? ReplacesOccurrenceId { get; init; }

    /// <summary>Gets the post whose resolution made the change; null for an operator's merge or split.</summary>
    public Guid? PostId { get; init; }
}
