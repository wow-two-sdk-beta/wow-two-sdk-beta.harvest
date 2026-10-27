using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents what one step execution produced: its outcome, captures, follow-up targets, link evidence and
/// usage.</summary>
public sealed record StepResult
{
    /// <summary>Gets what the host answered; <see cref="FetchOutcome.Ok"/> when the step did its work.</summary>
    public required FetchOutcome Outcome { get; init; }

    /// <summary>Gets the challenge's kind, for <see cref="FetchOutcome.Challenge"/>.</summary>
    public ChallengeKind? Challenge { get; init; }

    /// <summary>Gets the captures the step made.</summary>
    public IReadOnlyList<Capture> Captures { get; init; } = [];

    /// <summary>Gets the work the step enqueues for later steps.</summary>
    public IReadOnlyList<StepTarget> Next { get; init; } = [];

    /// <summary>Gets the step's evidence about known links.</summary>
    public IReadOnlyList<LinkObservation> Links { get; init; } = [];

    /// <summary>Gets whether an enumerate step listed everything, so absence from it is a weak negative.</summary>
    public bool EnumerationComplete { get; init; }

    /// <summary>Gets what the step spent.</summary>
    public StepUsage Usage { get; init; } = new();

    /// <summary>Gets how long the host asked to wait.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Gets a short description for the run's log.</summary>
    public string? Message { get; init; }
}
