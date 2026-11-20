using WoW.Two.Sdk.Beta.Be.Ai.Inferences;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Budget;

/// <summary>Holds the guard's arithmetic: the window, the state at a spend and the projection, with no I/O.</summary>
internal static class BudgetCalculator
{
    private const decimal MicroPerUsd = 1_000_000m;

    /// <summary>Resolves the window a clock time falls in: from <see cref="ProcessingBudgetOptions.SinceUtc"/> with no end
    /// when set, else the month that started on the latest cycle day at or before <paramref name="now"/>.</summary>
    /// <param name="options">The guard's settings.</param>
    /// <param name="now">The time to place.</param>
    internal static BudgetWindow Window(ProcessingBudgetOptions options, DateTimeOffset now)
    {
        if (options.SinceUtc is { } since)
            return new BudgetWindow(since.ToUniversalTime(), null);
        var utc = now.ToUniversalTime();
        var thisCycle = new DateTimeOffset(utc.Year, utc.Month, Math.Clamp(options.CycleStartDay, 1, 28), 0, 0, 0, TimeSpan.Zero);
        var start = utc >= thisCycle ? thisCycle : thisCycle.AddMonths(-1);
        return new BudgetWindow(start, start.AddMonths(1));
    }

    /// <summary>Names the state a spend is in: off without a limit, hard at the limit, soft at the soft threshold.</summary>
    /// <param name="spentMicroUsd">The spend in the window.</param>
    /// <param name="limitMicroUsd">The hard cap, or none.</param>
    /// <param name="softMicroUsd">The soft threshold, or none.</param>
    internal static BudgetState Classify(long spentMicroUsd, long? limitMicroUsd, long? softMicroUsd)
    {
        if (limitMicroUsd is not { } limit)
            return BudgetState.Off;
        if (spentMicroUsd >= limit)
            return BudgetState.Hard;
        return softMicroUsd is { } soft && spentMicroUsd >= soft ? BudgetState.Soft : BudgetState.Open;
    }

    /// <summary>Projects the spend of the whole monthly window at the pace so far: spent over elapsed, times the window's length.</summary>
    /// <param name="spentMicroUsd">The spend in the window.</param>
    /// <param name="window">The window.</param>
    /// <param name="now">The time of the reading.</param>
    /// <returns>The projected spend; none for a window with no end, or before any time has passed in it.</returns>
    internal static long? Projection(long spentMicroUsd, BudgetWindow window, DateTimeOffset now)
    {
        if (window.End is not { } end || now <= window.Start)
            return null;
        if (spentMicroUsd <= 0)
            return 0;
        decimal ratio = (decimal)(end - window.Start).Ticks / (now - window.Start).Ticks;
        return ratio > long.MaxValue / (decimal)spentMicroUsd
            ? long.MaxValue
            : (long)Math.Round(spentMicroUsd * ratio, MidpointRounding.AwayFromZero);
    }

    /// <summary>Converts a USD amount to micro-USD.</summary>
    /// <param name="usd">The amount.</param>
    internal static long Micro(decimal usd) => (long)Math.Round(usd * MicroPerUsd, MidpointRounding.AwayFromZero);

    /// <summary>Turns the log's per use case and model sums into rows.</summary>
    /// <param name="totals">The sums the inference log answered.</param>
    internal static IReadOnlyList<BudgetUsageRow> Rows(IEnumerable<InferenceTotals> totals) =>
        [.. totals.Select(total => new BudgetUsageRow
        {
            UseCase = total.UseCase, Model = $"{total.Provider}/{total.Model}", Attempts = total.Attempts, CostMicroUsd = total.CostMicroUsd,
        })];

    /// <summary>Builds the reading for a window's rows at a time.</summary>
    /// <param name="options">The guard's settings.</param>
    /// <param name="now">The time of the reading.</param>
    /// <param name="rows">The window's attempts and cost per use case and model.</param>
    internal static BudgetReading Compose(ProcessingBudgetOptions options, DateTimeOffset now, IReadOnlyList<BudgetUsageRow> rows)
    {
        var window = Window(options, now);
        long spent = rows.Sum(row => row.CostMicroUsd);
        long? limit = options.LimitUsd is { } usd ? Micro(usd) : null;
        long? soft = limit is { } cap ? (long)Math.Round(cap * options.SoftShare, MidpointRounding.AwayFromZero) : null;
        return new BudgetReading
        {
            State = Classify(spent, limit, soft), WindowStartUtc = window.Start, WindowEndUtc = window.End, LimitMicroUsd = limit,
            SoftMicroUsd = soft, SpentMicroUsd = spent, ProjectedMicroUsd = Projection(spent, window, now), ReadAtUtc = now, Rows = rows,
        };
    }
}

/// <summary>Represents the span a spend is counted over.</summary>
/// <param name="Start">When the window starts (UTC, inclusive).</param>
/// <param name="End">When a monthly window ends (UTC); none for a window that never resets.</param>
internal readonly record struct BudgetWindow(DateTimeOffset Start, DateTimeOffset? End);
