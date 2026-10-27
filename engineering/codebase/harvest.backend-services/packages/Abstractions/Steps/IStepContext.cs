using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Rendering;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Gives a step broker what its claim needs: a direct HTTP client, the renderer when the step renders, the
/// detectors and the clock.</summary>
/// <remarks>The engine builds one context per claim. A broker reports what the host answered through
/// <see cref="Observe"/>; a challenge, a block or a rate limit backs the host off and is never retried another way.</remarks>
public interface IStepContext
{
    /// <summary>Gets the resolved plan: the step, its lane and host, and its requirements.</summary>
    ResolvedPlan Plan { get; }

    /// <summary>Gets the source the step reads.</summary>
    SourceModel Source { get; }

    /// <summary>Gets a direct client under Harvest's user agent with the run's anonymous cookie jar; the caller does not
    /// dispose it.</summary>
    HttpClient Http { get; }

    /// <summary>Gets the renderer, when the step needs one and a renderer is bound.</summary>
    IBrowserRenderer? Browser { get; }

    /// <summary>Gets the detectors for the source, the adapter's own over the generic one.</summary>
    IChallengeDetector Challenges { get; }

    /// <summary>Gets the clock captures are stamped with.</summary>
    TimeProvider Clock { get; }

    /// <summary>Reports an observation to the host gate and the usage meters.</summary>
    /// <param name="signal">What the host answered.</param>
    void Observe(StepSignal signal);
}
