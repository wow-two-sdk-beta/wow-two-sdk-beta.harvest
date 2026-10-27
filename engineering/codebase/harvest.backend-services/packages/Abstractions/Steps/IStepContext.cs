using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Rendering;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Sessions;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Gives a step broker what the resolver leased for its claim: an HTTP client bound to the rung's exit, jar and
/// profile, the session, and the capabilities later rungs add.</summary>
/// <remarks>The engine builds one context per claim and releases its leases after the fenced result write.</remarks>
public interface IStepContext
{
    /// <summary>Gets the resolved plan: rung, leases and profile.</summary>
    ResolvedPlan Plan { get; }

    /// <summary>Gets the source the step reads.</summary>
    SourceModel Source { get; }

    /// <summary>Gets a client bound to the plan's exit, cookie jar and profile; the caller does not dispose it.</summary>
    HttpClient Http { get; }

    /// <summary>Gets the session leased for the claim.</summary>
    SessionLease? Session { get; }

    /// <summary>Gets the renderer, when the rung renders.</summary>
    IBrowserRenderer? Browser { get; }

    /// <summary>Gets the solver, when the rung may solve; each call reserves budget.</summary>
    IChallengeSolver? Solver { get; }

    /// <summary>Gets the detectors for the source, the adapter's own over the generic one.</summary>
    IChallengeDetector Challenges { get; }

    /// <summary>Gets the clock captures are stamped with.</summary>
    TimeProvider Clock { get; }

    /// <summary>Reports an observation to the ladder and the usage meters.</summary>
    /// <param name="signal">What the host answered.</param>
    void Observe(StepSignal signal);
}
