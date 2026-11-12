using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents a post one source shows, as the console lists it.</summary>
public sealed record SourcePostModel
{
    /// <summary>Gets the post's identifier.</summary>
    public required Guid PostId { get; init; }

    /// <summary>Gets the resolved title, else the source link's URL.</summary>
    public required string Title { get; init; }

    /// <summary>Gets what the post is; <see cref="PostKind.Other"/> when extraction named no kind.</summary>
    public required PostKind Kind { get; init; }

    /// <summary>Gets the post's rolled-up existence state.</summary>
    public required PostState State { get; init; }

    /// <summary>Gets the post's revision.</summary>
    public required int Revision { get; init; }

    /// <summary>Gets when the post was first captured (UTC).</summary>
    public required DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>Gets the post's last material change (UTC).</summary>
    public required DateTimeOffset LastChangedAt { get; init; }

    /// <summary>Gets the section whose run first captured the post from this source; null when no run did.</summary>
    public Guid? PipelineId { get; init; }

    /// <summary>Gets the event's start as the post states it, a partial ISO 8601 value.</summary>
    public string? StartsAt { get; init; }

    /// <summary>Gets the event's end as the post states it, a partial ISO 8601 value; null when it states none.</summary>
    public string? EndsAt { get; init; }

    /// <summary>Gets the URL of the source's first link to the post.</summary>
    public string? Url { get; init; }

    /// <summary>Gets the source URL of the first image in the post's resolved media; null when it has none. A stored
    /// copy (<see cref="PostMedia.FileRef"/>) is never used.</summary>
    public string? ImageUrl { get; init; }
}
