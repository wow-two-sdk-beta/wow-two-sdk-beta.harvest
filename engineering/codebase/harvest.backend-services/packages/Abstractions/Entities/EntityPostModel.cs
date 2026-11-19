namespace WoW.Two.Sdk.Beta.Be.Harvest.Entities;

/// <summary>Represents one post bound to an entity: what it did for the entity and where it was published. It carries
/// the post's title and link, never its text.</summary>
public sealed record EntityPostModel
{
    /// <summary>Gets the post's id; <c>GET api/posts/{id}</c> reads the post.</summary>
    public required Guid PostId { get; init; }

    /// <summary>Gets the relation's role, one of <see cref="EntityRoles"/>.</summary>
    public required string Role { get; init; }

    /// <summary>Gets what an <c>update</c> changed: <c>dates</c>, <c>place</c>, <c>price</c>, <c>status</c> and the like.</summary>
    public IReadOnlyList<string> Changes { get; init; } = [];

    /// <summary>Gets the occurrence the post is about, when it names one.</summary>
    public Guid? OccurrenceId { get; init; }

    /// <summary>Gets the year the post is about: its occurrence's start year, else the year it was posted, else the year
    /// it was first seen.</summary>
    public required int Year { get; init; }

    /// <summary>Gets the post's title, verbatim; null for a channel post without one.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the URL of the link that supplies the post's title.</summary>
    public string? Link { get; init; }

    /// <summary>Gets the source that published that link.</summary>
    public Guid? SourceId { get; init; }

    /// <summary>Gets that source's display name, else its handle.</summary>
    public string? Source { get; init; }

    /// <summary>Gets when the post was published, as a partial ISO 8601 string as precise as the source said.</summary>
    public string? PostedAt { get; init; }

    /// <summary>Gets when SCFD first captured the post.</summary>
    public required DateTimeOffset FirstSeenAtUtc { get; init; }

    /// <summary>Gets when the post was first bound to the entity.</summary>
    public required DateTimeOffset BoundAtUtc { get; init; }
}
