using System.Globalization;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Afisha;

/// <summary>Declares Afisha's public category, article and known-link check steps.</summary>
public sealed class AfishaSourceAdapter : ISourceAdapter, ISourceConfigurationProvider, ISourceSectionCatalog
{
    private static readonly TimeZoneInfo LocalTime = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tashkent");

    /// <inheritdoc />
    public SourceConfigurationModel DescribeConfiguration(SourceModel source)
    {
        return new()
        {
            DisplayName = "Afisha Uzbekistan",
            Message = "The source adapter is installed. Remote access is evaluated during each run.",
            ScopeSchema = SourceScopeSchemas.Catalog(Sections(source)),
            PostView = new() { ShowSourceText = true },
            PostFilters =
            [
                new(SourcePostFilterKeys.PublisherCategory, "Publisher category",
                    [.. AfishaUrls.Sections.Select(section => new SourcePostFilterOption(section.Key, section.Value))]),
                new(SourcePostFilterKeys.EventTiming, "Event timing",
                    [new("upcoming", "Upcoming"), new("ongoing", "Ongoing"), new("finished", "Finished"),
                        new("endUnknown", "End unknown"), new("undated", "Undated")]),
            ],
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<SourceSectionDefinition> Sections(SourceModel source) =>
        [.. from section in AfishaUrls.Sections
            select new SourceSectionDefinition(section.Value,
                $"https://{source.Handle}/{AfishaUrls.HarvestLocale}/{section.Key}", AfishaUrls.HarvestLocale)
            {
                Key = section.Key,
                // Afisha also publishes category entries at /{locale}/{date}/{slug}.
                RestrictToPath = false,
            }];

    /// <inheritdoc />
    public string? ReadPostFilter(string key, SourcePostModel post, DateTimeOffset asOf)
    {
        if (key == SourcePostFilterKeys.PublisherCategory && Uri.TryCreate(post.Url, UriKind.Absolute, out var url)
            && AfishaUrls.IsArticle(url) && url.Segments.Length >= 7)
        {
            string category = url.Segments[2].TrimEnd('/');
            return AfishaUrls.Sections.ContainsKey(category) ? category : null;
        }
        if (key != SourcePostFilterKeys.EventTiming || post.Kind != PostKind.Event)
            return null;
        var end = CalendarBoundary(post.EndsAt, end: true);
        if (end <= asOf)
            return "finished";
        if (CalendarBoundary(post.StartsAt, end: false) is not { } start)
            return "undated";
        if (start > asOf)
            return "upcoming";
        return end is null ? "endUnknown" : "ongoing";
    }

    private static DateTimeOffset? CalendarBoundary(string? value, bool end)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            if (date.Year is 1 or 9999) return null;
            if (end) date = date.AddDays(1);
            var midnight = date.ToDateTime(TimeOnly.MinValue);
            return new DateTimeOffset(midnight, LocalTime.GetUtcOffset(midnight));
        }
        if (value is not { Length: >= 16 } || value[10] != 'T')
            return null;
        var time = value[11..];
        if (time.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || time.Contains('+') || time.Contains('-'))
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant) ? instant : null;
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local) || local.Year is 1 or 9999)
            return null;
        return new DateTimeOffset(local, LocalTime.GetUtcOffset(local));
    }

    /// <inheritdoc />
    public string Key => "afisha-uz";

    /// <inheritdoc />
    public SourceFamily Family => SourceFamily.Site;

    /// <inheritdoc />
    public bool Serves(SourceModel source)
    {
        return source.Family == Family && AfishaUrls.IsHost(source.Handle);
    }

    /// <inheritdoc />
    public AdapterDescriptor Describe(SourceModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var requirements = new StepRequirements { MinDelay = TimeSpan.FromSeconds(2) };
        return new AdapterDescriptor
        {
            Steps =
            [
                new() { Key = "enumerate", Role = StepRole.Enumerate, Requires = requirements },
                new() { Key = "detail", Role = StepRole.Detail, Requires = requirements },
                new() { Key = "check", Role = StepRole.Check, Requires = requirements },
            ],
            Plans = new Dictionary<WorkKind, IReadOnlyList<string>>
            {
                [WorkKind.Capture] = ["enumerate", "detail"],
                [WorkKind.Discover] = ["enumerate", "detail"],
                [WorkKind.Sweep] = ["enumerate", "detail"],
                [WorkKind.Refresh] = ["detail"],
                [WorkKind.Link] = ["detail"],
                [WorkKind.Check] = ["check"],
            },
        };
    }
}
