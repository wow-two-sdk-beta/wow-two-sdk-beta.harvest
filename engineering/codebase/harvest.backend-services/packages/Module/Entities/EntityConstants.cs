namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Holds the names the entity layer shares with hosts, the store and its readers.</summary>
public static class EntityConstants
{
    /// <summary>The host configuration section of <see cref="EntityOptions"/>.</summary>
    public const string SectionName = "Harvest:Entities";

    /// <summary>The <c>harvest_post_work</c> kind that queues a post's resolution once a done extraction lands.</summary>
    public const string WorkKind = "entity-resolve";

    /// <summary>The entity type of an organized thing with dates and a place; one record across its editions.</summary>
    public const string EventType = "event";

    /// <summary>The entity type of an offered good, property, vehicle or service; one record across authors and sites.</summary>
    public const string ListingType = "listing";

    /// <summary>Gets the relation roles (scfd-entity-model #3.1), plus <c>unsure</c> when the extraction chose none.</summary>
    public static IReadOnlyList<string> Roles { get; } = ["announcement", "update", "report", "mention", "unsure"];

    /// <summary>Gets the cadences an event can take, most frequent last.</summary>
    public static IReadOnlyList<string> Cadences { get; } = ["annual", "semiannual", "quarterly", "monthly", "irregular", "unknown"];

    /// <summary>Gets the statuses an occurrence can take.</summary>
    public static IReadOnlyList<string> OccurrenceStatuses { get; } = ["scheduled", "cancelled", "postponed", "held", "unknown"];
}
