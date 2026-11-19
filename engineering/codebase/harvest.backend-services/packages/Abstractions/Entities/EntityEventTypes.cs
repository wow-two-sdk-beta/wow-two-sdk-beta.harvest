namespace WoW.Two.Sdk.Beta.Be.Harvest.Entities;

/// <summary>Holds the types of the entity change feed's events.</summary>
public static class EntityEventTypes
{
    /// <summary>Holds the type of a new entity: the first post about it.</summary>
    public const string EntityCreated = "entity.created";

    /// <summary>Holds the type of a change to the entity itself: a listing's <c>status</c> or <c>price</c>, an event's
    /// <c>cadence</c>.</summary>
    public const string EntityChanged = "entity.changed";

    /// <summary>Holds the type of an entity folded into another; <see cref="EntityEvent.RelatedEntityId"/> names the one
    /// that stays.</summary>
    public const string EntityMerged = "entity.merged";

    /// <summary>Holds the type of two entities an operator keeps apart; each gets one, naming the other.</summary>
    public const string EntitySplit = "entity.split";

    /// <summary>Holds the type of a new real occurrence.</summary>
    public const string OccurrenceAdded = "occurrence.added";

    /// <summary>Holds the type of a real occurrence whose dates, status (cancelled, postponed, held) or place changed.</summary>
    public const string OccurrenceChanged = "occurrence.changed";

    /// <summary>Holds the type of a predicted occurrence a source announced or reported, which is real now.</summary>
    public const string OccurrenceConfirmed = "occurrence.confirmed";

    /// <summary>Holds the type of a predicted next occurrence, new or replacing an earlier prediction.</summary>
    public const string OccurrencePredicted = "occurrence.predicted";

    /// <summary>Holds the type of a predicted occurrence withdrawn with no replacement.</summary>
    public const string OccurrenceRemoved = "occurrence.removed";

    /// <summary>Gets every type, in the order they are documented.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        EntityCreated, EntityChanged, EntityMerged, EntitySplit, OccurrenceAdded, OccurrenceChanged, OccurrenceConfirmed,
        OccurrencePredicted, OccurrenceRemoved,
    ];
}
