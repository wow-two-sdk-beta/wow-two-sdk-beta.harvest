namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents how far a step may go past a challenge: avoid and escalate always, solve and hand over only when
/// allowed.</summary>
public sealed record ChallengePolicy
{
    /// <summary>Gets whether the step may call a solver; off by default.</summary>
    public bool AllowSolve { get; init; }

    /// <summary>Gets the most a thousand solves may cost (USD).</summary>
    public decimal? SolverMaxUsdPer1k { get; init; }

    /// <summary>Gets how many solves a day the step may spend.</summary>
    public int? SolverMaxSolvesPerDay { get; init; }

    /// <summary>Gets whether the step may park an item for an operator.</summary>
    public bool AllowHuman { get; init; }
}
