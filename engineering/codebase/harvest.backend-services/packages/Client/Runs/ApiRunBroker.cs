using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Runs;

/// <summary>Controls durable runs through the same service contract as the in-process module.</summary>
internal sealed class ApiRunBroker(HttpClient http) : IRunBroker
{
    private const string Runs = "api/runs";

    /// <inheritdoc />
    public Task<Result<RunModel>> StartAsync(Guid pipelineId, WorkKind kind = WorkKind.Capture, CancellationToken cancellationToken = default) =>
        !Enum.IsDefined(kind) ? Task.FromResult(Result<RunModel>.Fail(AppErrorFactory.Validation("Unknown work kind.")))
            : HarvestDataHttp.SendAsync<RunModel>(http, HttpMethod.Post, Runs, new { PipelineId = pipelineId, Kind = kind }, cancellationToken);

    /// <inheritdoc />
    public Task<Result<RunModel>> GetAsync(Guid runId, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<RunModel>(http, HttpMethod.Get, $"{Runs}/{runId}", null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<RunModel>>> ListAsync(Guid pipelineId, int limit = 50, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<IReadOnlyList<RunModel>>(http, HttpMethod.Get, $"{Runs}?pipelineId={pipelineId}&limit={limit}", null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<RunModel>> CancelAsync(Guid runId, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<RunModel>(http, HttpMethod.Post, $"{Runs}/{runId}/cancel", null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<PipelineModel>> SetPausedAsync(Guid pipelineId, bool paused, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<PipelineModel>(http, HttpMethod.Post, $"api/pipelines/{pipelineId}/pause", new { Paused = paused }, cancellationToken);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<HostHealthModel>>> HostsAsync(Guid containerId, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<IReadOnlyList<HostHealthModel>>(http, HttpMethod.Get, $"api/hosts?containerId={containerId}", null, cancellationToken);
}
