using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.BirBir;

/// <summary>Declares BirBir rental collection and the listing schema shared with the existing import source.</summary>
public sealed class BirBirSourceAdapter : ISourceAdapter, ISourceConfigurationProvider, ISourceSectionCatalog
{
    /// <summary>The marketplace host.</summary>
    public const string Host = "birbir.uz";

    /// <summary>The query-free Tashkent apartment rental category.</summary>
    public const string RentalCatalog = "https://birbir.uz/ru/tashkent/cat/nedvizhimost/arenda/kvartiry";

    /// <inheritdoc />
    public string Key => "birbir-uz";

    /// <inheritdoc />
    public SourceFamily Family => SourceFamily.Site;

    /// <inheritdoc />
    public bool Serves(SourceModel source) =>
        source.Handle == Host && source.Family is SourceFamily.Site or SourceFamily.Manual;

    /// <inheritdoc />
    public SourceConfigurationModel DescribeConfiguration(SourceModel source) => new()
    {
        DisplayName = "BirBir",
        Readiness = CanCollect(source) ? SourceReadiness.Ready : SourceReadiness.ConfigurationRequired,
        Message = CanCollect(source) ? "Collection starts from the published rental category."
            : "Register BirBir as a readable site source with robots enabled.",
        ScopeSchema = SourceScopeSchemas.Catalog(Sections(source)),
        SourceFieldsSchema = BirBirSourceFields.JsonSchema,
    };

    /// <inheritdoc />
    public IReadOnlyList<SourceSectionDefinition> Sections(SourceModel source) => source.Family == Family
        ? [new("Tashkent apartment rentals", RentalCatalog, "ru")
            { Key = "apartments-rent", RestrictToPath = false, RegisterPassivePipeline = false }]
        : [];

    /// <inheritdoc />
    public AdapterDescriptor Describe(SourceModel source)
    {
        if (!CanCollect(source))
            return new() { Steps = [], Plans = new Dictionary<WorkKind, IReadOnlyList<string>>() };

        var pacing = new StepRequirements { MinDelay = TimeSpan.FromSeconds(3) };
        return new()
        {
            Steps =
            [
                new() { Key = "enumerate", Role = StepRole.Enumerate, Requires = pacing },
                new() { Key = "detail", Role = StepRole.Detail, Requires = pacing },
            ],
            Plans = new Dictionary<WorkKind, IReadOnlyList<string>>
            {
                [WorkKind.Capture] = ["enumerate", "detail"],
                [WorkKind.Discover] = ["enumerate", "detail"],
                [WorkKind.Link] = ["detail"],
            },
        };
    }

    private static bool CanCollect(SourceModel source) => source.Family == SourceFamily.Site
        && source.Permission is SourcePermission.Open or SourcePermission.Granted && source.RobotsPolicy == RobotsPolicy.Obey;
}
