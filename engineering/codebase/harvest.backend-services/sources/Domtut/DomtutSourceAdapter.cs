using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Domtut;

/// <summary>Declares Domtut's public catalog, detail, refresh and availability operations.</summary>
public sealed class DomtutSourceAdapter : ISourceAdapter, ISourceConfigurationProvider
{
    /// <inheritdoc />
    public SourceConfigurationModel DescribeConfiguration(SourceModel source) => new()
    {
        DisplayName = "Domtut",
        Message = "The source adapter is installed. Remote access is evaluated during each run.",
        ScopeSchema = SourceScopeSchemas.WebUrl("Save the complete Domtut catalog URL (catalog-nedvijimosty, one of its sections or a class page) or a listing URL, including every query filter."),
    };

    /// <inheritdoc />
    public string Key => "domtut-uz";
    /// <inheritdoc />
    public SourceFamily Family => SourceFamily.Site;
    /// <inheritdoc />
    public bool Serves(SourceModel source)
    {
        return source.Family == Family && DomtutUrls.IsHost(source.Handle);
    }

    /// <inheritdoc />
    public AdapterDescriptor Describe(SourceModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var requirements = new StepRequirements { MinDelay = TimeSpan.FromSeconds(3) };
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
