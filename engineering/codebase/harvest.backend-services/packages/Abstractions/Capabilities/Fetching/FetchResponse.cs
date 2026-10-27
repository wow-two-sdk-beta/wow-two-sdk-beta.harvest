using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;

/// <summary>Represents a direct fetch's content or its explicit refusal outcome.</summary>
public sealed record FetchResponse
{
    /// <summary>Gets what the host answered, without retrying through another route.</summary>
    public required FetchOutcome Outcome { get; init; }

    /// <summary>Gets the response, including a 404, 410 or 304, when a response arrived.</summary>
    public FetchedDocument? Document { get; init; }

    /// <summary>Gets the kind of challenge when one was detected.</summary>
    public ChallengeKind? Challenge { get; init; }

    /// <summary>Gets the minimum time before the host may be tried again.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Gets whether a conditional fetch returned 304; existing content remains authoritative.</summary>
    public bool NotModified { get; init; }

    /// <summary>Gets actual transport attempts and bytes read, including robots and redirect hops; cached body replay costs no bytes.</summary>
    public StepUsage Usage { get; init; } = new();

    /// <summary>Gets a short outcome description without URL query data or cookies.</summary>
    public string? Message { get; init; }
}
