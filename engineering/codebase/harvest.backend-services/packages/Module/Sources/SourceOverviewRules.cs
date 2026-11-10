using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;

/// <summary>Holds the overview's pure rules: a source's state, its daily series, a section's cadence and a host's health.</summary>
internal static class SourceOverviewRules
{
    /// <summary>Holds the failures in a row from which a source needs attention.</summary>
    internal const int AttentionFailures = 3;

    /// <summary>Gets the fetch outcomes that count as a refusal or a challenge.</summary>
    internal static IReadOnlySet<FetchOutcome> Refusals { get; } = new HashSet<FetchOutcome>
    {
        FetchOutcome.Challenge, FetchOutcome.WaitingRoom, FetchOutcome.Blocked, FetchOutcome.RateLimited,
        FetchOutcome.RobotsDisallowed, FetchOutcome.Restricted, FetchOutcome.LoginRequired,
    };

    /// <summary>Decides a source's state: harvesting while a run is queued or running; else needing attention when its
    /// host is paused, has failed <see cref="AttentionFailures"/> or more times in a row, or its last harvest failed;
    /// else healthy when a section is harvesting; else idle.</summary>
    /// <param name="runActive">Whether any of the source's runs is queued or running.</param>
    /// <param name="health">The source's host health; a pause in it is current.</param>
    /// <param name="lastHarvest">The source's newest capture run.</param>
    /// <param name="sections">The source's sections.</param>
    internal static SourceHarvestState State(bool runActive, SourceHealthModel health, SourceHarvestModel? lastHarvest,
        IEnumerable<SourceSectionModel> sections)
    {
        if (runActive)
            return SourceHarvestState.Harvesting;
        if (health.PausedUntil is not null || health.ConsecutiveFailures >= AttentionFailures || lastHarvest?.Status == RunStatus.Failed)
            return SourceHarvestState.NeedsAttention;
        return sections.Any(section => section.Harvesting) ? SourceHarvestState.Healthy : SourceHarvestState.Idle;
    }

    /// <summary>Returns the first moment the daily series counts: midnight UTC <see cref="SourceConstants.OverviewDays"/>
    /// − 1 days before today.</summary>
    /// <param name="now">The clock's time.</param>
    internal static DateTimeOffset SeriesStart(DateTimeOffset now) =>
        new DateTimeOffset(DateOnly.FromDateTime(now.UtcDateTime).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            .AddDays(-(SourceConstants.OverviewDays - 1));

    /// <summary>Spreads counts by UTC day over the last <see cref="SourceConstants.OverviewDays"/> days, oldest first,
    /// today last; days outside the window are dropped and missing days count zero.</summary>
    /// <param name="counts">The counts by UTC day.</param>
    /// <param name="now">The clock's time.</param>
    internal static IReadOnlyList<int> DailySeries(IReadOnlyDictionary<DateOnly, int> counts, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var series = new int[SourceConstants.OverviewDays];
        for (var index = 0; index < series.Length; index++)
            series[index] = counts.GetValueOrDefault(today.AddDays(index - (series.Length - 1)));
        return series;
    }

    /// <summary>Tells whether a section harvests: its pipeline is active and has an enabled capture schedule.</summary>
    /// <param name="pipeline">The section's pipeline.</param>
    internal static bool IsHarvesting(PipelineModel pipeline) =>
        pipeline.State == PipelineState.Active && Enabled(pipeline, WorkKind.Capture) is not null;

    /// <summary>Returns the enabled schedule row of a work kind, or null when that work is off.</summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="kind">The work kind.</param>
    internal static ScheduleModel? Enabled(PipelineModel pipeline, WorkKind kind) =>
        pipeline.Schedules.FirstOrDefault(schedule => schedule.Kind == kind && schedule.Enabled);

    /// <summary>Builds a host's health from its stored state and the source's last refusal; a pause that has ended
    /// reads as none.</summary>
    /// <param name="hostKey">The host key.</param>
    /// <param name="host">The host's stored state, or null when it has none yet.</param>
    /// <param name="refusal">The source's newest refused run.</param>
    /// <param name="now">The clock's time.</param>
    internal static SourceHealthModel Health(string hostKey, HostState? host, SourceRefusalModel? refusal, DateTimeOffset now)
    {
        DateTimeOffset? pausedUntil = null;
        string? reason = null;
        if (host?.PausedUntil is { } hostUntil && hostUntil > now)
            (pausedUntil, reason) = (hostUntil, host.PauseReason ?? host.LaneReason);
        if (host?.LanePausedUntil is { } laneUntil && laneUntil > now && (pausedUntil is null || laneUntil > pausedUntil))
            (pausedUntil, reason) = (laneUntil, host.LaneReason ?? host.PauseReason);
        return new SourceHealthModel
        {
            HostKey = hostKey,
            MinDelayMs = host?.MinDelayMs ?? SourceConstants.DefaultReadingDelayMs,
            PausedUntil = pausedUntil,
            PauseReason = pausedUntil is null ? null : reason,
            ConsecutiveFailures = Math.Max(host?.Failures ?? 0, host?.LaneFailures ?? 0),
            LastRefusal = refusal,
        };
    }

    /// <summary>Represents a host's stored pace, pause and failures, with the worst of its lanes.</summary>
    /// <param name="MinDelayMs">The least gap between requests.</param>
    /// <param name="PausedUntil">The host's own pause.</param>
    /// <param name="PauseReason">Why the host paused itself.</param>
    /// <param name="Failures">The host's failures in a row.</param>
    /// <param name="LanePausedUntil">The latest lane pause.</param>
    /// <param name="LaneReason">The challenge that paused that lane.</param>
    /// <param name="LaneFailures">The most failures in a row of any lane.</param>
    internal sealed record HostState(int MinDelayMs, DateTimeOffset? PausedUntil, string? PauseReason, int Failures,
        DateTimeOffset? LanePausedUntil, string? LaneReason, int LaneFailures);
}
