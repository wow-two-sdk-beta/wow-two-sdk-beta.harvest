using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;

/// <summary>Solves captchas through a vendor, for the solver rung; off by default and capped per step.</summary>
public interface IChallengeSolver
{
    /// <summary>Solves a captcha.</summary>
    /// <param name="request">The captcha to solve.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The token; <c>ExternalUnavailable</c> when the vendor failed or timed out.</returns>
    Task<Result<ChallengeSolution>> SolveAsync(ChallengeSolveRequest request, CancellationToken cancellationToken = default);
}
