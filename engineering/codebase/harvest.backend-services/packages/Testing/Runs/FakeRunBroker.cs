using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Runs;

/// <summary>Provides deterministic queue controls for consumers without running a worker or making network requests.</summary>
/// <param name="registry">The same registry fake the consumer uses.</param>
/// <param name="time">The clock that timestamps transitions; the system clock when omitted.</param>
public sealed class FakeRunBroker(FakeRegistrationBroker registry, TimeProvider? time = null) : IRunBroker
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, RunModel> runs = [];
    private readonly Dictionary<Guid, string> hosts = [];
    private TimeProvider Clock => time ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<Result<RunModel>> StartAsync(Guid pipelineId, WorkKind kind = WorkKind.Capture, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(kind))
            return Result<RunModel>.Fail(AppErrorFactory.Validation("Unknown work kind."));
        if (await registry.GetPipelineAsync(pipelineId, cancellationToken) is not Result<PipelineModel>.Success { Value: var pipeline })
            return Result<RunModel>.Fail(RegistrationErrors.PipelineNotFound(pipelineId));
        if (await registry.GetSourceAsync(pipeline.SourceId, cancellationToken) is not Result<SourceModel>.Success { Value: var source })
            return Result<RunModel>.Fail(RegistrationErrors.SourceNotFound(pipeline.SourceId));
        if (pipeline.State != PipelineState.Active)
            return Result<RunModel>.Fail(AppErrorFactory.Conflict("The pipeline is not active."));
        if (source.State != SourceState.Active || source.Permission is not (SourcePermission.Open or SourcePermission.Granted))
            return Result<RunModel>.Fail(AppErrorFactory.Forbidden("The source is not available for fetching."));

        lock (gate)
        {
            var existing = runs.Values.FirstOrDefault(run => run.PipelineId == pipelineId && run.Kind == kind
                && run.Status is RunStatus.Queued or RunStatus.Running or RunStatus.Paused);
            if (existing is not null)
                return Result<RunModel>.Ok(existing);
            var now = Clock.GetUtcNow();
            var sameSlot = runs.Values.FirstOrDefault(run => run.PipelineId == pipelineId && run.Kind == kind && run.QueuedAtUtc == now);
            if (sameSlot is not null)
                return Result<RunModel>.Ok(sameSlot);
            bool available = source.Family is SourceFamily.Site or SourceFamily.Page or SourceFamily.Structured or SourceFamily.Feed or SourceFamily.Link
                && kind is WorkKind.Capture or WorkKind.Discover or WorkKind.Refresh or WorkKind.Sweep or WorkKind.Check or WorkKind.Link;
            bool empty = kind is WorkKind.Check or WorkKind.Refresh;
            var run = new RunModel
            {
                Id = Guid.CreateVersion7(now), ContainerId = pipeline.ContainerId, PipelineId = pipelineId, Kind = kind,
                Status = !available ? RunStatus.Failed : empty ? RunStatus.Completed : RunStatus.Queued, QueuedAtUtc = now,
                FinishedAtUtc = !available || empty ? now : null,
                FetchOutcome = available ? null : FetchOutcome.CapabilityUnavailable,
                CompletionReason = !available ? RunReason.SourceOutcome : empty ? RunReason.NoItems : null,
                Message = available ? null : "No registered adapter offers this work kind.",
            };
            runs[run.Id] = run;
            if (available && !empty)
                hosts[run.Id] = source.Handle;
            return Result<RunModel>.Ok(run);
        }
    }

    /// <inheritdoc />
    public Task<Result<RunModel>> GetAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
            return Task.FromResult(runs.TryGetValue(runId, out var run) ? Result<RunModel>.Ok(run) : Missing());
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RunModel>>> ListAsync(Guid pipelineId, int limit = 50, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (limit is < 1 or > 200)
            return Result<IReadOnlyList<RunModel>>.Fail(AppErrorFactory.Validation("Limit must be between 1 and 200."));
        if (await registry.GetPipelineAsync(pipelineId, cancellationToken) is not Result<PipelineModel>.Success)
            return Result<IReadOnlyList<RunModel>>.Fail(RegistrationErrors.PipelineNotFound(pipelineId));
        lock (gate)
            return Result<IReadOnlyList<RunModel>>.Ok(runs.Values.Where(run => run.PipelineId == pipelineId)
                .OrderByDescending(run => run.QueuedAtUtc).ThenByDescending(run => run.Id).Take(limit).ToArray());
    }

    /// <inheritdoc />
    public Task<Result<RunModel>> CancelAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!runs.TryGetValue(runId, out var run))
                return Task.FromResult(Missing());
            if (run.Status is RunStatus.Queued or RunStatus.Running or RunStatus.Paused)
                runs[runId] = run = run with { Status = RunStatus.Cancelled, FinishedAtUtc = Clock.GetUtcNow(), CompletionReason = RunReason.Cancelled };
            return Task.FromResult(Result<RunModel>.Ok(run));
        }
    }

    /// <inheritdoc />
    public async Task<Result<PipelineModel>> SetPausedAsync(Guid pipelineId, bool paused, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await registry.GetPipelineAsync(pipelineId, cancellationToken) is not Result<PipelineModel>.Success { Value: var pipeline })
            return Result<PipelineModel>.Fail(RegistrationErrors.PipelineNotFound(pipelineId));
        var source = await registry.GetSourceAsync(pipeline.SourceId, cancellationToken);
        if (pipeline.State is not (PipelineState.Active or PipelineState.Paused)
            || pipeline.State == PipelineState.Paused && pipeline.PauseReason != PipelinePauseReason.Operator
            || !paused && source is not Result<SourceModel>.Success { Value: { State: SourceState.Active, Permission: SourcePermission.Open or SourcePermission.Granted } })
            return Result<PipelineModel>.Fail(AppErrorFactory.Conflict("The pipeline cannot change state with its current source permission."));
        return registry.SetPipelineState(pipelineId, paused ? PipelineState.Paused : PipelineState.Active,
            paused ? PipelinePauseReason.Operator : null);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<HostHealthModel>>> HostsAsync(Guid containerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await registry.GetContainerAsync(containerId, cancellationToken) is not Result<ContainerModel>.Success)
            return Result<IReadOnlyList<HostHealthModel>>.Fail(RegistrationErrors.ContainerNotFound(containerId));
        lock (gate)
            return Result<IReadOnlyList<HostHealthModel>>.Ok(runs.Values
                .Where(run => run.ContainerId == containerId && hosts.ContainsKey(run.Id)).GroupBy(run => hosts[run.Id])
                .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => new HostHealthModel
                {
                    HostKey = group.Key, Lane = StepConstants.DefaultLane,
                    Queued = group.Count(run => run.Status == RunStatus.Queued), Running = group.Count(run => run.Status == RunStatus.Running),
                }).ToArray());
    }

    private static Result<RunModel> Missing() => Result<RunModel>.Fail(AppErrorFactory.NotFound("The run was not found."));
}
