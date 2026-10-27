using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

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
}
