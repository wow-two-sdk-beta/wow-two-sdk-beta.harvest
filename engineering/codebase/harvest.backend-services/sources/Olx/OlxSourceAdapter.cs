using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Olx;

/// <summary>Declares OLX Uzbekistan's public catalog, detail, refresh and availability operations.</summary>
public sealed class OlxSourceAdapter : ISourceAdapter, ISourceConfigurationProvider, ISourceSectionCatalog
{
    private const string NewestFirst = "?search%5Border%5D=created_at%3Adesc";

    /// <summary>The published Tashkent long-term apartment catalog.</summary>
    public const string ApartmentRentCatalog = "https://www.olx.uz/nedvizhimost/kvartiry/arenda-dolgosrochnaya/tashkent/" + NewestFirst;

    /// <summary>The published Tashkent long-term house catalog.</summary>
    public const string HouseRentCatalog = "https://www.olx.uz/nedvizhimost/doma/arenda-dolgosrochnaya/tashkent/" + NewestFirst;

    /// <summary>The published Tashkent daily rental catalog.</summary>
    public const string DailyRentCatalog = "https://www.olx.uz/nedvizhimost/posutochno_pochasovo/tashkent/" + NewestFirst;

    /// <inheritdoc />
    public SourceConfigurationModel DescribeConfiguration(SourceModel source) => new()
    {
        DisplayName = "OLX Uzbekistan",
        Message = "The source adapter is installed. Remote access is evaluated during each run.",
        ScopeSchema = SourceScopeSchemas.Catalog(Sections(source)),
        SourceFieldsSchema = OlxSourceFields.JsonSchema,
        PostFilters = [new(SourcePostFilterKeys.Rooms, "Rooms", [])],
    };

    /// <inheritdoc />
    /// <remarks>Each section implies <c>listing</c> and its real-estate node, so a post these sections supply is tagged in code,
    /// not by a model: apartments, houses, and the daily rentals, whose ads cover flats, houses and rooms alike, under rent.</remarks>
    public IReadOnlyList<SourceSectionDefinition> Sections(SourceModel source) =>
    [
        Section("apartments-rent", "Tashkent long-term apartments", ApartmentRentCatalog, "real-estate/apartments"),
        Section("houses-rent", "Tashkent long-term houses", HouseRentCatalog, "real-estate/houses"),
        Section("daily-rent", "Tashkent daily rentals", DailyRentCatalog, "real-estate/real-estate-renting-and-leasing"),
    ];

    private static SourceSectionDefinition Section(string key, string name, string url, string domain) =>
        new(name, url, "und")
        {
            Key = key, RestrictToPath = false, RegisterPassivePipeline = false, ImpliedTags = new SectionImpliedTags("listing", domain),
        };

    /// <inheritdoc />
    public string Key => "olx-uz";
    /// <inheritdoc />
    public SourceFamily Family => SourceFamily.Site;
    /// <inheritdoc />
    public bool Serves(SourceModel source)
    {
        return source.Family == Family && OlxUrls.IsHost(source.Handle);
    }

    /// <inheritdoc />
    public AdapterDescriptor Describe(SourceModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var requirements = new StepRequirements { MinDelay = TimeSpan.FromSeconds(2) };
        return new()
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
