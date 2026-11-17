using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Represents one stage in a source section.</summary>
/// <param name="Kind">The stage's work kind.</param>
/// <param name="Available">Whether the source section can run this stage.</param>
/// <param name="Schedule">The saved schedule, if configured.</param>
/// <param name="Options">The effective batch and filter settings.</param>
/// <param name="Pending">The number of checkpoint pages or eligible detail targets, when counted.</param>
/// <param name="Runs">Recent runs of this stage, latest first.</param>
public sealed record PipelineStageModel(WorkKind Kind, bool Available, ScheduleModel? Schedule,
    PipelineStageOptions Options, long? Pending, IReadOnlyList<RunModel> Runs)
{
    /// <summary>Gets the published entry link for discovery.</summary>
    public SourceSectionDefinition? Catalog { get; init; }

    /// <summary>Gets the configured processor choices for a Process stage.</summary>
    public IReadOnlyList<string> ProcessorKeys { get; init; } = [];

    /// <summary>Gets the console name of each key in <see cref="ProcessorKeys"/>.</summary>
    public IReadOnlyDictionary<string, string> ProcessorLabels { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the operations in the stage's current execution plan.</summary>
    public IReadOnlyList<string> OperationKeys { get; init; } = [];
}
