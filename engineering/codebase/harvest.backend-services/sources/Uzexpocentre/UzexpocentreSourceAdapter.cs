using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Uzexpocentre;

/// <summary>Reads the venue's explicit exhibition calendar without assuming future recurrence.</summary>
public sealed class UzexpocentreSourceAdapter : ISourceAdapter, ISourceConfigurationProvider
{
    /// <inheritdoc />
    public SourceConfigurationModel DescribeConfiguration(SourceModel source) => new()
    {
        DisplayName = "Uzexpocentre exhibitions",
        Message = "The source adapter is installed. Remote access is evaluated during each run.",
        ScopeSchema = SourceScopeSchemas.WebUrl("Save the venue calendar URL or a known exhibition URL."),
    };

    /// <inheritdoc />
    public string Key => "uzexpocentre-uz";
    /// <inheritdoc />
    public SourceFamily Family => SourceFamily.Site;
    /// <inheritdoc />
    public bool Serves(SourceModel source) => source.Family == Family && source.Handle is "uzexpocentre.uz" or "www.uzexpocentre.uz";
    /// <inheritdoc />
    public AdapterDescriptor Describe(SourceModel source) => new()
    {
        Steps =
        [
            new() { Key = "enumerate", Role = StepRole.Enumerate, Requires = new() { MinDelay = TimeSpan.FromSeconds(2) } },
            new() { Key = "detail", Role = StepRole.Detail, Requires = new() { MinDelay = TimeSpan.FromSeconds(2) } },
            new() { Key = "check", Role = StepRole.Check, Requires = new() { MinDelay = TimeSpan.FromSeconds(2) } },
        ],
        Plans = new Dictionary<WorkKind, IReadOnlyList<string>>
        {
            [WorkKind.Capture] = ["enumerate"], [WorkKind.Discover] = ["enumerate"],
            [WorkKind.Sweep] = ["enumerate"], [WorkKind.Refresh] = ["detail"],
            [WorkKind.Link] = ["detail"], [WorkKind.Check] = ["check"],
        },
    };
}
