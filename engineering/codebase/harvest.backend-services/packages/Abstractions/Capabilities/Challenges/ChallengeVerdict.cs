using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;

/// <summary>Represents what a detector found in place of content — a challenge, a block or a rate limit — with the
/// detector that saw it and the marker that matched.</summary>
public sealed record ChallengeVerdict
{
    /// <summary>Gets what the response is: <see cref="FetchOutcome.Challenge"/>, <see cref="FetchOutcome.WaitingRoom"/>,
    /// <see cref="FetchOutcome.LoginRequired"/>, <see cref="FetchOutcome.Blocked"/> or
    /// <see cref="FetchOutcome.RateLimited"/>.</summary>
    public required FetchOutcome Outcome { get; init; }

    /// <summary>Gets the kind of check, for <see cref="FetchOutcome.Challenge"/>.</summary>
    public ChallengeKind? Kind { get; init; }

    /// <summary>Gets how long the host asked to wait, from <c>Retry-After</c>.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Gets the detector that saw it.</summary>
    public required string DetectorKey { get; init; }

    /// <summary>Gets the marker that matched, for the evidence log.</summary>
    public required string Evidence { get; init; }
}
