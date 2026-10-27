namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Represents a stored schedule row of a pipeline.</summary>
public sealed record ScheduleModel
{
    /// <summary>Gets the row's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the work the row runs.</summary>
    public required WorkKind Kind { get; init; }

    /// <summary>Gets how the row decides when its work is due.</summary>
    public required ScheduleMode Mode { get; init; }

    /// <summary>Gets the interval, or the shortest adaptive one.</summary>
    public TimeSpan? Every { get; init; }

    /// <summary>Gets the longest adaptive interval.</summary>
    public TimeSpan? MaxEvery { get; init; }

    /// <summary>Gets the cron expression.</summary>
    public string? Cron { get; init; }

    /// <summary>Gets the IANA time zone the cron expression and the window read in.</summary>
    public required string TimeZone { get; init; }

    /// <summary>Gets the daily window the work may run in.</summary>
    public ScheduleWindow? Window { get; init; }

    /// <summary>Gets whether the row is live.</summary>
    public required bool Enabled { get; init; }

    /// <summary>Gets when the row's next run is due (UTC); null until the scheduler first computes it.</summary>
    public DateTimeOffset? NextDueAtUtc { get; init; }
}
