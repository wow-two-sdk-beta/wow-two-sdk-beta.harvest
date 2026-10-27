namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Refers to how a schedule row decides when its work is due.</summary>
public enum ScheduleMode
{
    /// <summary>Only on request.</summary>
    Manual,

    /// <summary>Every fixed interval, at or above the source's floor.</summary>
    Interval,

    /// <summary>On a cron expression in an IANA time zone.</summary>
    Cron,

    /// <summary>Between a minimum and a maximum interval, by the change rate.</summary>
    Adaptive,
}
