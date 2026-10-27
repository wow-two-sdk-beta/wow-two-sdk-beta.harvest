namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Executes one step of one adapter — the broker a source adapter implements per step.</summary>
/// <remarks>A broker never retries a challenge, a block or a rate limit: it reports the outcome, and the host gate backs
/// off. It returns an outcome rather than throwing for anything a host can answer.</remarks>
public interface IStepBroker
{
    /// <summary>Gets the key of the adapter the step belongs to.</summary>
    string AdapterKey { get; }

    /// <summary>Gets the step's key within its adapter.</summary>
    string StepKey { get; }

    /// <summary>Executes the step for one claimed item.</summary>
    /// <param name="item">The claimed item.</param>
    /// <param name="context">The client, renderer and detectors for the claim.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The step's outcome, captures, follow-up targets, link evidence and usage.</returns>
    Task<StepResult> ExecuteAsync(StepItem item, IStepContext context, CancellationToken cancellationToken = default);
}
