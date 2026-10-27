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

    /// <summary>Holds the most items a filtered run may capture.</summary>
    public const int MaxItemsCap = 2000;

    /// <summary>Holds the longest display name.</summary>
    public const int MaxNameLength = 200;

    /// <summary>Holds the longest canonical scope key.</summary>
    public const int MaxScopeKeyLength = 2000;

    /// <summary>Gets the shortest interval a schedule may run at.</summary>
    public static TimeSpan MinInterval { get; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets the work kinds a schedule row may run.</summary>
    public static IReadOnlyList<WorkKind> SchedulableKinds { get; } =
        [WorkKind.Capture, WorkKind.Discover, WorkKind.Refresh, WorkKind.Check, WorkKind.Metrics];
}
