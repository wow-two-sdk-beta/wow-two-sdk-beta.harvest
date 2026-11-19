using WoW.Two.Sdk.Beta.Be.Harvest.Entities;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Entities;

/// <summary>Describes an entity a test writes straight into an implementation's store, as resolution would have left it:
/// its occurrences and the posts bound to it. Seeding appends the change events resolution appends for a new entity:
/// <c>entity.created</c>, then <c>occurrence.added</c> or <c>occurrence.predicted</c> per occurrence, earliest first.</summary>
public sealed record EntitySeed
{
    /// <summary>Gets the entity's id.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Gets the container it belongs to.</summary>
    public required Guid ContainerId { get; init; }

    /// <summary>Gets its type, one of <see cref="EntityTypes"/>.</summary>
    public string EntityType { get; init; } = EntityTypes.Event;

    /// <summary>Gets its canonical English name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the other names sources gave it.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Gets its primary taxonomy node.</summary>
    public string? PrimaryDomain { get; init; }

    /// <summary>Gets its status.</summary>
    public string Status { get; init; } = "active";

    /// <summary>Gets its cadence.</summary>
    public string Cadence { get; init; } = "unknown";

    /// <summary>Gets its occurrences; a predicted one is <c>estimated</c>.</summary>
    public IReadOnlyList<OccurrenceModel> Occurrences { get; init; } = [];

    /// <summary>Gets the posts bound to it.</summary>
    public IReadOnlyList<EntityRelationSeed> Relations { get; init; } = [];

    /// <summary>Gets when a post first and last bound to it.</summary>
    public required DateTimeOffset SeenAtUtc { get; init; }
}

/// <summary>Describes one post's binding to a seeded entity.</summary>
public sealed record EntityRelationSeed
{
    /// <summary>Gets the post, which the store already holds in the entity's container.</summary>
    public required Guid PostId { get; init; }

    /// <summary>Gets the role, one of <see cref="EntityRoles"/>.</summary>
    public string Role { get; init; } = EntityRoles.Announcement;

    /// <summary>Gets what an <c>update</c> changed.</summary>
    public IReadOnlyList<string> Changes { get; init; } = [];

    /// <summary>Gets the occurrence the post is about.</summary>
    public Guid? OccurrenceId { get; init; }

    /// <summary>Gets when the post was bound.</summary>
    public required DateTimeOffset BoundAtUtc { get; init; }
}
