using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Hata;

/// <summary>Declares HATA's public Tashkent long-term apartment rental search.</summary>
public sealed class HataSourceAdapter : ISourceAdapter, ISourceConfigurationProvider, ISourceSectionCatalog
{
    /// <summary>The public HATA listing host.</summary>
    public const string Host = "hata.uz";

    internal const string CatalogPath = "/listings/rent/flats/tashkent";

    /// <summary>The first page of HATA's Tashkent apartment rental catalog.</summary>
    public const string RentCatalog = "https://" + Host + CatalogPath;

    /// <inheritdoc />
    public string Key => "hata-uz";

    /// <inheritdoc />
    public SourceFamily Family => SourceFamily.Site;

    /// <inheritdoc />
    public bool Serves(SourceModel source)
    {
        return source.Family == Family && source.Handle == Host;
    }

    /// <inheritdoc />
    public SourceConfigurationModel DescribeConfiguration(SourceModel source)
    {
        return new()
        {
            DisplayName = "HATA",
            Message = "Reads the public Tashkent long-term apartment rental catalog and its listing details.",
            ScopeSchema = SourceScopeSchemas.Catalog(Sections(source)),
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<SourceSectionDefinition> Sections(SourceModel source)
    {
        return [new("Tashkent apartment rentals", RentCatalog, "ru")
        {
            Key = "apartments-rent",
            RestrictToPath = false,
            RegisterPassivePipeline = false,
            ImpliedTags = new SectionImpliedTags("listing", "real-estate/apartments"),
        }];
    }

    /// <inheritdoc />
    public AdapterDescriptor Describe(SourceModel source)
    {
        var requirements = new StepRequirements { MinDelay = TimeSpan.FromSeconds(5) };
        return new()
        {
            Steps =
            [
                new() { Key = "enumerate", Role = StepRole.Enumerate, Requires = requirements },
                new() { Key = "detail", Role = StepRole.Detail, Requires = requirements },
            ],
            Plans = new Dictionary<WorkKind, IReadOnlyList<string>>
            {
                [WorkKind.Capture] = ["enumerate", "detail"],
                [WorkKind.Discover] = ["enumerate", "detail"],
                [WorkKind.Sweep] = ["enumerate", "detail"],
            },
        };
    }
}
