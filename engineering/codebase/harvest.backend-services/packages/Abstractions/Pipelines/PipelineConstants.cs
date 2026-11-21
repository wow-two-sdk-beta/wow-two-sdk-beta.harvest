namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Holds the limits of pipeline declarations.</summary>
public static class PipelineConstants
{
    /// <summary>Holds the scope key of a pipeline over the whole source.</summary>
    public const string WholeScopeKey = "*";

    /// <summary>Holds the time zone schedules read in unless they state one.</summary>
    public const string DefaultTimeZone = "Asia/Tashkent";

    /// <summary>Holds the most listing pages a filtered run may read.</summary>
    public const int MaxPagesCap = 100;

    /// <summary>Holds the most listing pages an update may set.</summary>
    public const int MaxPagesOnUpdate = 50;

    /// <summary>Holds the most items a filtered run may capture.</summary>
    public const int MaxItemsCap = 2000;

    /// <summary>Holds the most items a harvest stage's batch may hold.</summary>
    public const int MaxHarvestBatch = 500;

    /// <summary>Holds the most parallel fetches a harvest stage may run.</summary>
    public const int MaxParallel = 16;

    /// <summary>Holds the longest request gap, in milliseconds, a stage may set.</summary>
    public const int MaxGapMs = 60000;

    /// <summary>Holds the longest display name.</summary>
    public const int MaxNameLength = 200;

    /// <summary>Holds the longest canonical scope key.</summary>
    public const int MaxScopeKeyLength = 2000;

    /// <summary>Gets the shortest interval a schedule may run at, for every kind but harvest; see <see cref="MinIntervalFor"/>.</summary>
    public static TimeSpan MinInterval { get; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets the shortest interval a harvest schedule may run at.</summary>
    public static TimeSpan MinHarvestInterval { get; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets the shortest interval a schedule of a kind may run at.</summary>
    /// <param name="kind">The schedule's work kind.</param>
    /// <returns><see cref="MinHarvestInterval"/> for <see cref="WorkKind.Harvest"/>, else <see cref="MinInterval"/>.</returns>
    public static TimeSpan MinIntervalFor(WorkKind kind) => kind == WorkKind.Harvest ? MinHarvestInterval : MinInterval;

    /// <summary>Gets the work kinds a schedule row may run.</summary>
    public static IReadOnlyList<WorkKind> SchedulableKinds { get; } =
        [WorkKind.Capture, WorkKind.Discover, WorkKind.Refresh, WorkKind.Check, WorkKind.Metrics, WorkKind.Harvest, WorkKind.Process];
}
