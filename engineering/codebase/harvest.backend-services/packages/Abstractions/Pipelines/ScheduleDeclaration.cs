namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Represents one cadence row of a pipeline: a work kind with its mode, spec and window.</summary>
/// <remarks>A pipeline holds at most one row per kind; a whole-site sweep is a <see cref="WorkKind.Discover"/>, a
/// <see cref="WorkKind.Refresh"/> and a <see cref="WorkKind.Check"/> row.</remarks>
public sealed record ScheduleDeclaration
{
    /// <summary>Gets the work the row runs: capture, discover, refresh, check or metrics.</summary>
    public required WorkKind Kind { get; init; }

    /// <summary>Gets how the row decides when its work is due.</summary>
    public required ScheduleMode Mode { get; init; }

    /// <summary>Gets the interval for <see cref="ScheduleMode.Interval"/>, or the shortest one for
    /// <see cref="ScheduleMode.Adaptive"/>; at least <see cref="PipelineConstants.MinInterval"/>.</summary>
    public TimeSpan? Every { get; init; }

    /// <summary>Gets the longest interval for <see cref="ScheduleMode.Adaptive"/>.</summary>
    public TimeSpan? MaxEvery { get; init; }

    /// <summary>Gets the five- or six-field cron expression for <see cref="ScheduleMode.Cron"/>.</summary>
    public string? Cron { get; init; }

    /// <summary>Gets the IANA time zone the cron expression and the window read in.</summary>
    public string TimeZone { get; init; } = PipelineConstants.DefaultTimeZone;

    /// <summary>Gets the daily window the work may run in; any time when null.</summary>
    public ScheduleWindow? Window { get; init; }

    /// <summary>Gets whether the row is live.</summary>
    public bool Enabled { get; init; } = true;
}
