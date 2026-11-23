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

    /// <summary>Gets the locale of this source's post link, or its extracted language.</summary>
    public string? Language { get; init; }

    /// <summary>Gets the event's start as the post states it, a partial ISO 8601 value.</summary>
    public string? StartsAt { get; init; }

    /// <summary>Gets the event's end as the post states it, a partial ISO 8601 value; null when it states none.</summary>
    public string? EndsAt { get; init; }

    /// <summary>Gets when the post itself was published at its source, a partial ISO 8601 value; null when its reader
    /// cannot tell.</summary>
    public string? PostedAt { get; init; }

    /// <summary>Gets the URL of the source's first link to the post.</summary>
    public string? Url { get; init; }

    /// <summary>Gets the confirmed content match to another external URL, when one exists.</summary>
    public PostDuplicateModel? Duplicate { get; init; }

    /// <summary>Gets the first image address, using the private stored copy when available.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>Gets the post's image addresses in order. Listings include their full gallery. Other posts include at most
    /// <see cref="SourceConstants.MaxPostImages"/>. The first address is <see cref="ImageUrl"/>.</summary>
    public IReadOnlyList<string> ImageUrls { get; init; } = [];

    /// <summary>Gets the essential housing facts for a card, when a housing projection exists.</summary>
    public SourceListingSummary? Listing { get; init; }

    /// <summary>Gets the publisher's category label when the source adapter has item-level evidence.</summary>
    public string? PublisherCategory { get; init; }

    /// <summary>Gets the source adapter's timing at the query's reference time.</summary>
    public string? EventTiming { get; init; }
}

/// <summary>Housing facts returned with a source's post list.</summary>
/// <param name="PropertyKind">The kind of property offered.</param>
/// <param name="Rooms">The stated number of rooms.</param>
/// <param name="TotalAreaM2">The total area in square metres.</param>
/// <param name="Floor">The signed unit level.</param>
/// <param name="BuildingFloors">The number of floors in the building.</param>
/// <param name="City">The published city.</param>
/// <param name="District">The published district.</param>
/// <param name="Transaction">The transaction kind.</param>
/// <param name="Period">The price period.</param>
/// <param name="Price">The published asking price.</param>
public sealed record SourceListingSummary(ListingPropertyKind? PropertyKind, short? Rooms, decimal? TotalAreaM2,
    short? Floor, short? BuildingFloors, string? City, string? District, ListingTransaction? Transaction,
    ListingPeriod? Period, ListingMoney? Price);
