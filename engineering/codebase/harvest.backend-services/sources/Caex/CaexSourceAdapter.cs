using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Caex;

/// <summary>Collects the explicitly dated events in the public CAEx calendar.</summary>
public sealed class CaexSourceAdapter : ISourceAdapter, ISourceConfigurationProvider
{
    /// <summary>The stable private reader key.</summary>
    public const string AdapterKey = "caex-uz";

    internal const string Host = "caexuzbekistan.com";
    internal const string CalendarPath = "/events-calendar";

    /// <inheritdoc />
    public string Key => AdapterKey;

    /// <inheritdoc />
    public SourceFamily Family => SourceFamily.Site;

    /// <inheritdoc />
    public bool Serves(SourceModel source) => source.Family == Family && source.Handle == Host;

    /// <inheritdoc />
    public SourceConfigurationModel DescribeConfiguration(SourceModel source) => new()
    {
        DisplayName = "CAEx venue calendar",
        Message = "Calendar rows supply venue dates, not exhibitor admission. Access is checked on each run.",
        ScopeSchema = SourceScopeSchemas.WebUrl($"https://{Host}{CalendarPath}/"),
    };

    /// <inheritdoc />
    public AdapterDescriptor Describe(SourceModel source) => new()
    {
        Steps = [new() { Key = "enumerate", Role = StepRole.Enumerate, Requires = new() { MinDelay = TimeSpan.FromSeconds(2) } }],
        Plans = new Dictionary<WorkKind, IReadOnlyList<string>>
        {
            [WorkKind.Capture] = ["enumerate"],
            [WorkKind.Discover] = ["enumerate"],
            [WorkKind.Sweep] = ["enumerate"],
        },
    };
}
