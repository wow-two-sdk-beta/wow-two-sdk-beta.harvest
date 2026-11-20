using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Selects collection activity consistently for the console and its consumer fakes.</summary>
public static class SourceHarvestSelection
{
    /// <summary>Tells whether work contributes to collection or processing activity.</summary>
    public static bool IsCollection(WorkKind kind) =>
        kind is WorkKind.Capture or WorkKind.Discover or WorkKind.Harvest or WorkKind.Process;

    /// <summary>Tells whether the source may execute collection work: active, open or granted, and turned on by the instance.</summary>
    public static bool CanRead(SourceModel source) => source.State == SourceState.Active && source.TurnedOn
        && source.Permission is SourcePermission.Open or SourcePermission.Granted;

    /// <summary>Gets enabled automatic collection schedules, excluding individually disabled stages.</summary>
    public static IEnumerable<ScheduleModel> Automatic(PipelineModel pipeline) => pipeline.Schedules
        .Where(schedule => IsCollection(schedule.Kind) && schedule.Enabled
            && schedule.Mode != ScheduleMode.Manual && schedule.Stage?.Disabled != true);

    /// <summary>Gets the section's manual action: its legacy capture path, else the saved harvest stage, discovery,
    /// or processing. Turning automation off does not disable an otherwise enabled manual stage.</summary>
    public static WorkKind? ManualKind(PipelineModel pipeline)
    {
        if (pipeline.State != PipelineState.Active)
            return null;
        if (pipeline.Schedules.Any(schedule => schedule.Kind == WorkKind.Capture && schedule.Enabled))
            return WorkKind.Capture;
        foreach (var kind in new[] { WorkKind.Harvest, WorkKind.Discover, WorkKind.Process })
            if (pipeline.Schedules.Any(schedule => schedule.Kind == kind && schedule.Stage?.Disabled != true))
                return kind;
        return null;
    }
}
