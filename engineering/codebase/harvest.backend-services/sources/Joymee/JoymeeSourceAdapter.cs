using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Joymee;

/// <summary>Declares Joymee's public Tashkent apartment rental search.</summary>
public sealed class JoymeeSourceAdapter : ISourceAdapter, ISourceConfigurationProvider, ISourceSectionCatalog
{
    /// <summary>The public host serving the JSON catalog and listings.</summary>
    public const string Host = "api.joymee.uz";
    /// <summary>The current Tashkent apartment rental catalog, sorted by newest.</summary>
    public const string RentCatalog = "https://api.joymee.uz/api/v1/announcement/?deal_type=2&property_type=1&region=59&ordering=newest&page=1&perPagePlain=5";
    internal const string Path = "/api/v1/announcement/";

    /// <inheritdoc />
    public string Key => "joymee-uz";
    /// <inheritdoc />
    public SourceFamily Family => SourceFamily.Site;

    /// <inheritdoc />
    public bool Serves(SourceModel source) => source.Family == Family && source.Handle == Host;

    /// <inheritdoc />
    public SourceConfigurationModel DescribeConfiguration(SourceModel source) => new()
    {
        DisplayName = "Joymee",
        Message = "Reads the public Tashkent long-term apartment rental catalog.",
        ScopeSchema = SourceScopeSchemas.Catalog(Sections(source)),
        SourceFieldsSchema = JoymeeSourceFields.JsonSchema,
    };

    /// <inheritdoc />
    public IReadOnlyList<SourceSectionDefinition> Sections(SourceModel source) =>
        [new("Tashkent apartment rentals", RentCatalog, "und")
            { Key = "apartments-rent", RestrictToPath = false, RegisterPassivePipeline = false }];

    /// <inheritdoc />
    public AdapterDescriptor Describe(SourceModel source)
    {
        var requirements = new StepRequirements { MinDelay = TimeSpan.FromSeconds(3) };
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
