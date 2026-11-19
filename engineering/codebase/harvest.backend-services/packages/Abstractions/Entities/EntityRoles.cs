namespace WoW.Two.Sdk.Beta.Be.Harvest.Entities;

/// <summary>Holds what a post does for its entity (scfd-entity-model #3.1).</summary>
public static class EntityRoles
{
    /// <summary>Holds the role of a post that states the entity's facts.</summary>
    public const string Announcement = "announcement";

    /// <summary>Holds the role of a post that changes a fact already known.</summary>
    public const string Update = "update";

    /// <summary>Holds the role of a post that covers an edition while or after it happens.</summary>
    public const string Report = "report";

    /// <summary>Holds the role of a post that refers to the entity without its facts.</summary>
    public const string Mention = "mention";

    /// <summary>Holds the role of a post whose extraction chose none.</summary>
    public const string Unsure = "unsure";

    /// <summary>Gets every role.</summary>
    public static IReadOnlyList<string> All { get; } = [Announcement, Update, Report, Mention, Unsure];
}
