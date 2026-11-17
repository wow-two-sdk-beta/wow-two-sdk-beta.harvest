using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Rendering;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
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

    /// <summary>Gets the pipeline's saved publisher or filter scope, frozen when the run was queued.</summary>
    PipelineScope Scope => new();

    /// <summary>Gets the pipeline's run limits, frozen when the run was queued.</summary>
    PipelineLimits Limits => new();

    /// <summary>Gets a direct client under Harvest's user agent with the run's anonymous cookie jar; the caller does not
    /// dispose it.</summary>
    HttpClient Http { get; }

    /// <summary>Gets the renderer, when the step needs one and a renderer is bound.</summary>
    IBrowserRenderer? Browser { get; }

    /// <summary>Gets the detectors for the source, the adapter's own over the generic one.</summary>
    IChallengeDetector Challenges { get; }

    /// <summary>Gets the clock captures are stamped with.</summary>
    TimeProvider Clock { get; }

    /// <summary>Tells whether a page destination belongs to the run's selected section.</summary>
    bool AllowsDestination(Uri destination) => true;

    /// <summary>Reserves one provider request under the resolved host's durable permission and pace. Use this for
    /// SDK/provider calls that do not pass through Harvest's already-paced HTTP client.</summary>
    /// <param name="cancellationToken">The token that cancels waiting for the shared host.</param>
    /// <returns>A lease held through the provider response; send only when its outcome is <see cref="FetchOutcome.Ok"/>.</returns>
    Task<ISourceRequestLease> AcquireRequestAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This step context does not provide durable request pacing.");

    /// <summary>Runs a named operation and lets the host record its timing and result.</summary>
    /// <typeparam name="T">The value returned by the operation.</typeparam>
    /// <param name="key">A stable statement of the operation, such as fetch or deserialize.</param>
    /// <param name="execute">The work itself, with its regular cancellation policy.</param>
    /// <param name="describe">Summarizes the result without sensitive content. Omit for a plain completion.</param>
    /// <returns>The value from <paramref name="execute"/>.</returns>
    Task<T> RunOperationAsync<T>(string key, Func<Task<T>> execute, Func<T, StepOperationResult>? describe = null)
        => execute();

    /// <summary>Reports an observation to the host gate and the usage meters.</summary>
    /// <param name="signal">What the host answered.</param>
    void Observe(StepSignal signal);
}
