using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Runs;

/// <summary>Queues, inspects and cancels durable pipeline executions.</summary>
public interface IRunBroker
{
    /// <summary>Queues a manual execution; concurrent requests join an existing active run.</summary>
    Task<Result<RunModel>> StartAsync(Guid pipelineId, WorkKind kind = WorkKind.Capture, CancellationToken cancellationToken = default);
    /// <summary>Reads a run.</summary>
    Task<Result<RunModel>> GetAsync(Guid runId, CancellationToken cancellationToken = default);
    /// <summary>Lists the most recent executions of a pipeline.</summary>
    Task<Result<IReadOnlyList<RunModel>>> ListAsync(Guid pipelineId, int limit = 50, CancellationToken cancellationToken = default);
    /// <summary>Requests cancellation and fences outstanding work.</summary>
    Task<Result<RunModel>> CancelAsync(Guid runId, CancellationToken cancellationToken = default);
    /// <summary>Pauses or resumes a pipeline without discarding its history.</summary>
    Task<Result<PipelineModel>> SetPausedAsync(Guid pipelineId, bool paused, CancellationToken cancellationToken = default);
    /// <summary>Reads host health limited to a container.</summary>
    Task<Result<IReadOnlyList<HostHealthModel>>> HostsAsync(Guid containerId, CancellationToken cancellationToken = default);
    /// <summary>Sets the least gap between two requests to a source's host, which every source on that host shares.</summary>
    /// <param name="sourceId">The source whose host to pace.</param>
    /// <param name="minDelayMs">The gap in milliseconds, <see cref="SourceConstants.MinReadingDelayMs"/>–<see cref="SourceConstants.MaxReadingDelayMs"/>.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The source's host health; <c>NotFound</c> for an unknown source, <c>Validation</c> for a gap out of range.</returns>
    Task<Result<SourceHealthModel>> SetReadingPaceAsync(Guid sourceId, int minDelayMs, CancellationToken cancellationToken = default);
}
