using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Craftfair;

/// <summary>Declares the public Hunarmand event directory and its individual calls.</summary>
public sealed class CraftfairSourceAdapter : ISourceAdapter, ISourceConfigurationProvider
{
    /// <inheritdoc />
    public SourceConfigurationModel DescribeConfiguration(SourceModel source) => new()
    {
        DisplayName = "Hunarmand event directory",
        Message = "The source adapter is installed. Remote access is evaluated during each run.",
        ScopeSchema = SourceScopeSchemas.WebUrl("Save the public Craftfair directory or an individual event call URL."),
    };

    /// <inheritdoc />
    public string Key => "craftfair-uz";
    /// <inheritdoc />
    public SourceFamily Family => SourceFamily.Site;
    /// <inheritdoc />
    public bool Serves(SourceModel source) => source.Family == Family && source.Handle is "craftfair.uz" or "www.craftfair.uz";
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
            [WorkKind.Capture] = ["enumerate", "detail"], [WorkKind.Discover] = ["enumerate", "detail"],
            [WorkKind.Sweep] = ["enumerate", "detail"], [WorkKind.Refresh] = ["detail"],
            [WorkKind.Link] = ["detail"], [WorkKind.Check] = ["check"],
        },
    };
}
