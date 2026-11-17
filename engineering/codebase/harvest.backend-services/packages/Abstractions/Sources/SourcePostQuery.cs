namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Search and facet selections for one source's posts.</summary>
public sealed record SourcePostQuery
{
    /// <summary>The initial and minimum adaptive batch used by the console.</summary>
    public const int DefaultLimit = 30;

    /// <summary>The largest continuation page.</summary>
    public const int MaxLimit = 90;

    /// <summary>Gets the case-insensitive substring of the post's title to match.</summary>
    public string? Search { get; init; }

    /// <summary>Gets one selected value per facet key. Different facets combine with AND.</summary>
    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the maximum number of results to return.</summary>
    public int Limit { get; init; } = DefaultLimit;

    /// <summary>Gets the continuation token returned for the same source, search and selections.</summary>
    public string? Cursor { get; init; }

    /// <summary>Gets an optional exact post ID for reading that post's source-specific summary.</summary>
    public Guid? PostId { get; init; }
}

/// <summary>A choice exposed by a source's facet definition.</summary>
/// <param name="Value">The stable value to send when selected.</param>
/// <param name="Label">The name shown to the operator.</param>
public sealed record SourcePostFilterOption(string Value, string Label);

/// <summary>Declares a facet over stored posts. Empty options are discovered from the source's posts.</summary>
/// <param name="Key">The stable facet identifier.</param>
/// <param name="Label">The name shown to the operator.</param>
/// <param name="Options">Known choices, including choices whose current count is zero.</param>
public sealed record SourcePostFilterDefinition(string Key, string Label, IReadOnlyList<SourcePostFilterOption> Options);

/// <summary>The available choices and counts for one facet.</summary>
/// <param name="Key">The stable facet identifier.</param>
/// <param name="Label">The name shown to the operator.</param>
/// <param name="Options">Choices counted under the search and every other selected facet.</param>
public sealed record SourcePostFacet(string Key, string Label, IReadOnlyList<SourcePostFacetOption> Options);

/// <summary>One facet choice with its server-computed count.</summary>
/// <param name="Value">The value to send when selected.</param>
/// <param name="Label">The name shown to the operator.</param>
/// <param name="Count">The number of matching posts before pagination.</param>
public sealed record SourcePostFacetOption(string Value, string Label, int Count);

/// <summary>A page of a source's matching posts with counts for the full search.</summary>
/// <param name="Items">The matching posts, newest first.</param>
/// <param name="NextCursor">The next page token, or null at the end.</param>
/// <param name="Total">The number of posts matching search and selections.</param>
/// <param name="SearchTotal">The number of posts matching search alone.</param>
/// <param name="Facets">The facet choices counted across all pages.</param>
public sealed record SourcePostPage(IReadOnlyList<SourcePostModel> Items, string? NextCursor, int Total, int SearchTotal, IReadOnlyList<SourcePostFacet> Facets)
{
    /// <summary>Gets the page size to use for a first or slow continuation read.</summary>
    public int DefaultPageSize => SourcePostQuery.DefaultLimit;

    /// <summary>Gets the upper bound for faster continuation reads.</summary>
    public int MaxPageSize => SourcePostQuery.MaxLimit;
}

/// <summary>Stable identifiers shared by source adapters and the console Posts query.</summary>
public static class SourcePostFilterKeys
{
    /// <summary>Post kind.</summary>
    public const string Kind = "kind";
    /// <summary>Language of the source post.</summary>
    public const string Language = "language";
    /// <summary>Section where the source post was harvested.</summary>
    public const string Section = "section";
    /// <summary>When the post was first seen.</summary>
    public const string Harvested = "harvested";
    /// <summary>Availability of the post.</summary>
    public const string Availability = "availability";
    /// <summary>Category published on the source post.</summary>
    public const string PublisherCategory = "publisherCategory";
    /// <summary>Timing of an event relative to the query's reference time.</summary>
    public const string EventTiming = "eventTiming";
    /// <summary>The normalized number of rooms in a housing listing.</summary>
    public const string Rooms = "rooms";
}
