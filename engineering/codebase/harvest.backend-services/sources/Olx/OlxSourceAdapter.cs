using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Olx;

/// <summary>Declares OLX Uzbekistan's public catalog, detail, refresh and availability operations.</summary>
public sealed class OlxSourceAdapter : ISourceAdapter, ISourceConfigurationProvider
{
    /// <inheritdoc />
    public SourceConfigurationModel DescribeConfiguration(SourceModel source) => new()
    {
        DisplayName = "OLX Uzbekistan",
        Message = "The source adapter is installed. Remote access is evaluated during each run.",
        ScopeSchema = SourceScopeSchemas.WebUrl("Save the complete OLX category, search, seller or ad URL, including every query filter."),
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
