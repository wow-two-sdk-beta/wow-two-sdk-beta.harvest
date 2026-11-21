using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters;

internal static class ScheduleProvisioning
{
    /// <summary>Enables profile cadences while keeping the section's staged path and operator settings.
    /// An existing capture row on a staged section is kept disabled so it can be reused later.
    /// A pipeline whose schedules were turned off automatically (<see cref="PipelineModel.AutoOff"/>) gets none: it returns null
    /// and stays as it is until an operator turns a row back on.</summary>
    internal static List<ScheduleDeclaration>? EnabledCadence(PipelineModel pipeline, IReadOnlyList<ScheduleDeclaration> defaults)
    {
        if (pipeline.AutoOff is not null)
            return null;
        bool staged = pipeline.Schedules.Any(row => row.Kind is WorkKind.Discover or WorkKind.Harvest
            && row.Enabled && row.Mode != ScheduleMode.Manual && row.Stage?.Disabled is not true);
        var missing = defaults.Where(row => row.Enabled && (!staged || row.Kind != WorkKind.Capture))
            .ToDictionary(row => row.Kind);
        bool changed = false;
        var cadence = new List<ScheduleDeclaration>();
        foreach (var row in pipeline.Schedules)
        {
            bool requested = missing.Remove(row.Kind);
            bool enabled = !(staged && row.Kind == WorkKind.Capture) && (row.Enabled || requested);
            changed |= enabled != row.Enabled;
            cadence.Add(new()
            {
                Kind = row.Kind, Mode = row.Mode, Every = row.Every, MaxEvery = row.MaxEvery, Cron = row.Cron,
                TimeZone = row.TimeZone, Window = row.Window, Enabled = enabled, Stage = row.Stage,
            });
        }
        cadence.AddRange(missing.Values);
        return changed || missing.Count > 0 ? cadence : null;
    }
}
