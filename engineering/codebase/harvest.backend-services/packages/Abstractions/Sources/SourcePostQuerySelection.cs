using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using static WoW.Two.Sdk.Beta.Be.Harvest.Sources.SourcePostFilterKeys;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Applies the shared Posts query across storage implementations. Accepts summaries in newest-first order
/// by first capture and post ID and retains only one page of matching summaries.</summary>
public sealed class SourcePostQuerySelection
{
    private static readonly SourcePostFilterOption[] HarvestWindows =
    [
        new("1", "Last 24 hours"), new("7", "Last 7 days"), new("30", "Last 30 days"),
    ];

    private readonly SourcePostQuery query;
    private readonly string signature;
    private readonly Cursor? after;
    private readonly ISourceConfigurationProvider? provider;
    private readonly IReadOnlyDictionary<Guid, SourceSectionDefinition> sections;
    private readonly Facet[] facets;
    private readonly SourcePostFilterDefinition? categories;
    private readonly string[][] values;
    private readonly List<SourcePostModel> page = [];
    private int total;
    private int searchTotal;

    private SourcePostQuerySelection(SourceModel source, SourcePostQuery query, DateTimeOffset now,
        IReadOnlyList<PipelineModel> pipelines, ISourceConfigurationProvider? provider)
    {
        this.query = query with { Search = query.Search?.Trim() ?? "" };
        this.provider = provider;
        signature = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            source.Id, this.query.Search, query.PostId, Filters = query.Filters.OrderBy(pair => pair.Key, StringComparer.Ordinal),
        })));
        if (query.Cursor is { } text)
            after = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(text));
        AsOf = (after?.AsOf ?? now).ToUniversalTime();

        var catalog = (provider as ISourceSectionCatalog)?.Sections(source) ?? [];
        sections = pipelines.Where(pipeline => pipeline.SourceId == source.Id).ToDictionary(pipeline => pipeline.Id, pipeline =>
            catalog.FirstOrDefault(section => SameSection(section.Url, pipeline.Scope.Url))
            ?? new(pipeline.Scope.Url is null ? "Whole source" : pipeline.Name ?? "Section", pipeline.Scope.Url ?? "", "") { Key = pipeline.Id.ToString() });
        var sectionOptions = catalog.Concat(sections.Values).Select(section => new SourcePostFilterOption(SectionValue(section), section.Label))
            .DistinctBy(option => option.Value).ToArray();
        var declared = provider?.DescribeConfiguration(source).PostFilters ?? [];
        categories = declared.FirstOrDefault(definition => definition.Key == PublisherCategory);
        SourcePostFilterDefinition[] definitions =
        [
            new(Kind, "Post type", []),
            new(Language, "Language", [.. catalog.Select(section => section.Locale).Distinct().Select(locale => new SourcePostFilterOption(locale, Label(Language, locale)))]),
            new(Section, "Harvest section", sectionOptions),
            new(Harvested, "First harvested", HarvestWindows),
            new(Availability, "Availability", [new("live", "Available"), new("suspect", "Needs check"), new("gone", "Removed")]),
            .. declared,
        ];
        facets = definitions.DistinctBy(definition => definition.Key)
            .Select(definition => new Facet(definition, query.Filters.GetValueOrDefault(definition.Key))).ToArray();
        values = new string[facets.Length][];
    }

    /// <summary>Gets the effective time for date filters, fixed across a cursor's pages.</summary>
    public DateTimeOffset AsOf { get; }

    /// <summary>Gets whether the selected adapter supports filtering by normalized listing room count.</summary>
    public bool UsesListingRooms => facets.Any(facet => facet.Key == Rooms);

    /// <summary>Validates filters and starts a query against a known source.</summary>
    /// <param name="source">The selected source.</param>
    /// <param name="query">The requested filters, search and page.</param>
    /// <param name="now">The time of the first page request.</param>
    /// <param name="pipelines">The source's registered harvest sections.</param>
    /// <param name="provider">The selected adapter's optional facet definitions.</param>
    /// <returns>A selection ready to count and page summaries.</returns>
    public static Result<SourcePostQuerySelection> Start(SourceModel source, SourcePostQuery query, DateTimeOffset now,
        IReadOnlyList<PipelineModel> pipelines, ISourceConfigurationProvider? provider = null)
    {
        if (query.Limit is < 1 or > SourcePostQuery.MaxLimit || query.Search?.Length > 200
            || query.Cursor?.Length > 1024 || query.Filters is null || query.Filters.Count > 16
            || query.Filters.Any(pair => pair.Key.Length > 40 || string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > 200))
            return Result<SourcePostQuerySelection>.Fail(RegistrationErrors.Invalid("The Posts query exceeds its request limits."));
        SourcePostQuerySelection selection;
        try
        {
            selection = new(source, query, now, pipelines, provider);
        }
        catch (Exception error) when (error is JsonException or FormatException)
        {
            return Result<SourcePostQuerySelection>.Fail(RegistrationErrors.Invalid("The Posts cursor is invalid."));
        }
        if (query.Cursor is not null && (selection.after is not { } after || after.Signature != selection.signature
                || after.PostId == Guid.Empty || after.At > after.AsOf || after.AsOf > now))
            return Result<SourcePostQuerySelection>.Fail(RegistrationErrors.Invalid("The Posts cursor belongs to a different query."));
        foreach (var (key, value) in query.Filters)
        {
            var facet = selection.facets.FirstOrDefault(option => option.Key == key);
            if (facet is null || !facet.Accepts(value)
                || (key == Rooms && (!short.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var rooms) || rooms is < 0 or > 250)))
                return Result<SourcePostQuerySelection>.Fail(RegistrationErrors.Invalid($"The Posts filter '{key}' or its value is unsupported."));
        }
        return Result<SourcePostQuerySelection>.Ok(selection);
    }

    /// <summary>Counts a stored summary across facets and retains it when it belongs to the current page.</summary>
    /// <param name="post">The next source post in newest-first order.</param>
    public void Add(SourcePostModel post)
    {
        if ((query.PostId is { } id && id != post.PostId) || post.FirstSeenAt > AsOf
            || !post.Title.Contains(query.Search!, StringComparison.OrdinalIgnoreCase))
            return;
        searchTotal++;
        var failed = -1;
        sections.TryGetValue(post.PipelineId ?? Guid.Empty, out var section);
        for (var index = 0; index < facets.Length; index++)
        {
            var facet = facets[index];
            values[index] = Values(facet.Key, post, section);
            if (facet.Selected is null || values[index].Contains(facet.Selected, StringComparer.Ordinal)) continue;
            failed = failed == -1 ? index : -2;
            if (failed == -2) break;
        }
        if (failed == -2) return;
        for (var index = 0; index < facets.Length; index++)
            if (failed == -1 || failed == index)
                foreach (var value in values[index])
                    facets[index].Count(value);
        if (failed != -1) return;
        total++;
        if (page.Count <= query.Limit && (after is null || post.FirstSeenAt < after.At
                || post.FirstSeenAt == after.At && post.PostId.CompareTo(after.PostId) < 0))
            page.Add(post);
    }

    /// <summary>Returns the page with counts. Storage may hydrate the retained summaries with their media.</summary>
    /// <returns>The page in the same order the source reads posts.</returns>
    public SourcePostPage Complete()
    {
        var items = page.Take(query.Limit).ToArray();
        string? next = page.Count > query.Limit
            ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Cursor(signature, AsOf, items[^1].FirstSeenAt, items[^1].PostId)))
            : null;
        return new(items, next, total, searchTotal, facets.Select(facet => facet.Build()).ToArray());
    }

    /// <summary>Returns the publisher category label for a stored post when the adapter supplied evidence.</summary>
    /// <param name="post">The source's saved post.</param>
    /// <returns>A label from the declared category facet.</returns>
    public string? CategoryLabel(SourcePostModel post)
    {
        var value = categories is null ? null : provider?.ReadPostFilter(PublisherCategory, post, AsOf);
        return value is null ? null : categories!.Options.FirstOrDefault(option => option.Value == value)?.Label ?? value;
    }

    private string[] Values(string key, SourcePostModel post, SourceSectionDefinition? section)
    {
        if (key == Harvested)
            return [.. HarvestWindows.Where(option => (AsOf - post.FirstSeenAt).TotalDays <= int.Parse(option.Value, CultureInfo.InvariantCulture)).Select(option => option.Value)];
        string? value = key switch
        {
            Kind => post.Kind.ToString().ToLowerInvariant(),
            Language => post.Language ?? section?.Locale,
            Section => section is not null ? SectionValue(section) : null,
            Availability => post.State.ToString().ToLowerInvariant(),
            Rooms => post.Listing?.Rooms?.ToString(CultureInfo.InvariantCulture),
            _ => provider?.ReadPostFilter(key, post, AsOf),
        };
        return string.IsNullOrWhiteSpace(value) ? [] : [value];
    }

    private static string SectionValue(SourceSectionDefinition section) => section.Key ?? section.Label;

    private static bool SameSection(string expected, string? actual) =>
        Uri.TryCreate(expected, UriKind.Absolute, out var left)
        && Uri.TryCreate(actual, UriKind.Absolute, out var right)
        && Host(left) == Host(right)
        && left.PathAndQuery.TrimEnd('/') == right.PathAndQuery.TrimEnd('/');

    private static string Host(Uri uri) => uri.IdnHost.StartsWith("www.", StringComparison.Ordinal) ? uri.IdnHost[4..] : uri.IdnHost;

    private static string Label(string key, string value) => (key, value) switch
    {
        (Kind, "event") => "Events",
        (Kind, "listing") => "Listings",
        (Kind, "other") => "Others",
        (Language, "ru") => "Russian",
        (Language, "uz") => "Uzbek",
        (Language, "en") => "English",
        (Rooms, _) => value == "1" ? "1 room" : value + " rooms",
        _ => char.ToUpperInvariant(value[0]) + value[1..],
    };

    private sealed record Cursor(string Signature, DateTimeOffset AsOf, DateTimeOffset At, Guid PostId);

    private sealed class Facet(SourcePostFilterDefinition definition, string? selected)
    {
        private readonly Dictionary<string, SourcePostFacetOption> options =
            definition.Options.ToDictionary(option => option.Value, option => new SourcePostFacetOption(option.Value, option.Label, 0), StringComparer.Ordinal);

        internal string Key => definition.Key;
        internal string? Selected { get; } = selected;
        internal bool Accepts(string value) => Key == Language || definition.Options.Count == 0 || options.ContainsKey(value);

        internal void Count(string value)
        {
            if (!options.TryGetValue(value, out var current))
                current = new(value, Label(Key, value), 0);
            options[value] = current with { Count = current.Count + 1 };
        }

        internal SourcePostFacet Build()
        {
            if (Selected is { } value && !options.ContainsKey(value))
                options.Add(value, new(value, Label(Key, value), 0));
            return new(Key, definition.Label, Key == Rooms
                ? [.. options.Values.OrderBy(option => short.Parse(option.Value, CultureInfo.InvariantCulture))]
                : [.. options.Values]);
        }
    }
}
