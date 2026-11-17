namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Serializes a provider request under the run's shared host permission and pacing policy.</summary>
public interface ISourceRequestLease : IAsyncDisposable
{
    /// <summary>Gets whether the request may proceed: only <see cref="FetchOutcome.Ok"/> permits provider I/O.</summary>
    FetchOutcome Outcome { get; }

    /// <summary>Gets the remaining host pause when admission was refused.</summary>
    TimeSpan? RetryAfter { get; }

    /// <summary>Records the completed provider outcome before releasing its host reservation.</summary>
    /// <param name="outcome">The mapped provider outcome; the engine handles refusal backoff.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task CompleteAsync(FetchOutcome outcome, CancellationToken cancellationToken = default);
}
