namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Represents the daily window a schedule's work may run in, in the schedule's time zone; it may wrap midnight.</summary>
public sealed record ScheduleWindow
{
    /// <summary>Gets when the window opens.</summary>
    public required TimeOnly Start { get; init; }

    /// <summary>Gets when the window closes; before <see cref="Start"/> when the window wraps midnight.</summary>
    public required TimeOnly End { get; init; }
}
