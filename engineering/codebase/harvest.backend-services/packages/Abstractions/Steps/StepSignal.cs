using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents one observation a step reports while it runs, which the ladder and the usage meters learn from.</summary>
public sealed record StepSignal
{
    /// <summary>Gets what the host answered.</summary>
    public required FetchOutcome Outcome { get; init; }

    /// <summary>Gets the HTTP status.</summary>
    public int? HttpStatus { get; init; }

    /// <summary>Gets the challenge's kind, for <see cref="FetchOutcome.Challenge"/>.</summary>
    public ChallengeKind? Challenge { get; init; }

    /// <summary>Gets the bytes read.</summary>
    public long Bytes { get; init; }

    /// <summary>Gets the request's latency.</summary>
    public TimeSpan? Latency { get; init; }

    /// <summary>Gets how long the host asked to wait.</summary>
    public TimeSpan? RetryAfter { get; init; }
}
