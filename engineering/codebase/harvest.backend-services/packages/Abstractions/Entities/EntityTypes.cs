namespace WoW.Two.Sdk.Beta.Be.Harvest.Entities;

/// <summary>Holds the entity types (scfd-entity-model #4).</summary>
public static class EntityTypes
{
    /// <summary>Holds the type of an organized thing with dates and a place; one record across its editions.</summary>
    public const string Event = "event";

    /// <summary>Holds the type of an offered good, property, vehicle or service; one record across authors and sites.</summary>
    public const string Listing = "listing";

    /// <summary>Gets every type.</summary>
    public static IReadOnlyList<string> All { get; } = [Event, Listing];
}
