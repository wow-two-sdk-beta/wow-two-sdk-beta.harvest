using System.Globalization;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Budget;

/// <summary>Says how a pass that calls a model behaves at the current spend.</summary>
public enum BudgetState
{
    /// <summary>No limit is set, or the host composes no inference log: nothing changes.</summary>
    Off,

    /// <summary>The spend is under the soft threshold: every pass runs as configured.</summary>
    Open,

    /// <summary>The spend reached the soft threshold: a call tries only the last (cheapest) configured route.</summary>
    Soft,

    /// <summary>The spend reached the limit: no queued row is claimed and no operator run calls a model.</summary>
    Hard,
}

/// <summary>Represents one reading of the AI spend against its limit in the current window.</summary>
/// <remarks>Amounts are micro-USD, with the same figures in USD (four places) beside them for people.</remarks>
public sealed record BudgetReading
{
    /// <summary>Gets what the passes do at this spend.</summary>
    public required BudgetState State { get; init; }

    /// <summary>Gets when the window started (UTC): the cycle's start day, or <see cref="ProcessingBudgetOptions.SinceUtc"/>.</summary>
    public required DateTimeOffset WindowStartUtc { get; init; }

    /// <summary>Gets when the monthly window ends (UTC); none for a window that starts at <see cref="ProcessingBudgetOptions.SinceUtc"/>.</summary>
    public DateTimeOffset? WindowEndUtc { get; init; }

    /// <summary>Gets the hard cap in micro-USD; none when no limit is set.</summary>
    public long? LimitMicroUsd { get; init; }

    /// <summary>Gets the soft threshold in micro-USD, the limit times the soft share; none when no limit is set.</summary>
    public long? SoftMicroUsd { get; init; }

    /// <summary>Gets the cost in micro-USD of every inference in the window, over all use cases and scopes — evals included.</summary>
    public required long SpentMicroUsd { get; init; }

    /// <summary>Gets the spend the whole monthly window reaches at the pace so far, in micro-USD; none for a window with no end.</summary>
    public long? ProjectedMicroUsd { get; init; }

    /// <summary>Gets when the log was summed (UTC); a reading kept after a failed read keeps its old time.</summary>
    public required DateTimeOffset ReadAtUtc { get; init; }

    /// <summary>Gets whether the log could not be read and no earlier reading existed, so the guard failed closed; the spend
    /// is then unknown, not zero.</summary>
    public bool ReadFailed { get; init; }

    /// <summary>Gets the window's attempts and cost per use case and model, in key order.</summary>
    public IReadOnlyList<BudgetUsageRow> Rows { get; init; } = [];

    /// <summary>Gets <see cref="SpentMicroUsd"/> in USD, four places.</summary>
    public decimal SpentUsd => Usd(SpentMicroUsd);

    /// <summary>Gets <see cref="LimitMicroUsd"/> in USD, four places.</summary>
    public decimal? LimitUsd => LimitMicroUsd is { } micro ? Usd(micro) : null;

    /// <summary>Gets <see cref="SoftMicroUsd"/> in USD, four places.</summary>
    public decimal? SoftUsd => SoftMicroUsd is { } micro ? Usd(micro) : null;

    /// <summary>Gets <see cref="ProjectedMicroUsd"/> in USD, four places.</summary>
    public decimal? ProjectedUsd => ProjectedMicroUsd is { } micro ? Usd(micro) : null;

    /// <summary>Picks the routes a call may try: all of them in order until the soft threshold, then only the last, the
    /// cheapest by the host's own ordering.</summary>
    /// <param name="models">The configured routes, each <c>{provider}/{model}</c>, in order.</param>
    /// <returns>The routes to pass to the runner; <paramref name="models"/> itself when it holds one route or the guard is not soft.</returns>
    public IReadOnlyList<string> Routes(IReadOnlyList<string> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        return State == BudgetState.Soft && models.Count > 1 ? [models[^1]] : models;
    }

    /// <summary>Words the refusal of a call made while the state is <see cref="BudgetState.Hard"/>.</summary>
    /// <returns>A sentence naming the spend, the limit and the setting to raise.</returns>
    public string RefusalMessage() => ReadFailed
        ? "The AI spend could not be read from the inference log, so model calls are held back; check the AI module's database."
        : string.Create(CultureInfo.InvariantCulture,
            $"The AI spend limit for this window is reached ({SpentUsd} of {LimitUsd ?? 0m} USD); raise {ProcessingBudgetConstants.SectionName}:LimitUsd or wait for the next window.");

    private static decimal Usd(long microUsd)
    {
        long units = (long)Math.Round(Math.Abs(microUsd) / 100m, MidpointRounding.AwayFromZero);
        return new decimal((int)units, (int)(units >> 32), 0, microUsd < 0, 4);
    }
}

/// <summary>Represents the attempts and cost of one use case on one model in the window.</summary>
public sealed record BudgetUsageRow
{
    /// <summary>Gets the use case key.</summary>
    public required string UseCase { get; init; }

    /// <summary>Gets the route, <c>{provider}/{model}</c>.</summary>
    public required string Model { get; init; }

    /// <summary>Gets the attempts, failed ones included.</summary>
    public long Attempts { get; init; }

    /// <summary>Gets the cost in micro-USD.</summary>
    public long CostMicroUsd { get; init; }
}
