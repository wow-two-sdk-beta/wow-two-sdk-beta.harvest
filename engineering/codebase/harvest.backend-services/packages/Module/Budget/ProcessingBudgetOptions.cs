namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Budget;

/// <summary>Configures the AI spend guard, bound from <see cref="ProcessingBudgetConstants.SectionName"/>.</summary>
/// <remarks>The guard compares the host's inference log with <see cref="LimitUsd"/> over one window: a month that starts on
/// <see cref="CycleStartDay"/>, or everything since <see cref="SinceUtc"/>. Without a limit it is off and changes nothing.</remarks>
public sealed class ProcessingBudgetOptions
{
    /// <summary>Gets or sets the hard cap for the window in USD; none turns the guard off.</summary>
    public decimal? LimitUsd { get; set; }

    /// <summary>Gets or sets the share of <see cref="LimitUsd"/> at which the guard turns soft: over
    /// <c>0</c> and up to <c>1</c>.</summary>
    public decimal SoftShare { get; set; } = 0.8m;

    /// <summary>Gets or sets the day of the month, 1 to 28, on which the monthly window starts at 00:00 UTC.</summary>
    public int CycleStartDay { get; set; } = 1;

    /// <summary>Gets or sets when a one-off window starts: the window is then <c>[SinceUtc, now)</c> and never resets,
    /// whatever <see cref="CycleStartDay"/> says. None keeps the monthly window.</summary>
    public DateTimeOffset? SinceUtc { get; set; }

    /// <summary>Gets or sets how long a computed reading is reused, so the workers' polling never sums the log per claim.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);
}
