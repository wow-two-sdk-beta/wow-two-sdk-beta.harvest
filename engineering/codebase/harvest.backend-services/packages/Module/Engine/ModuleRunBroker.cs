using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Implements execution control over the durable engine tables.</summary>
internal sealed class ModuleRunBroker(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource database,
    IRegistrationBroker registry,
    IEnumerable<ISourceAdapter> adapters,
    TimeProvider clock) : IRunBroker
{
    public async Task<Result<RunModel>> StartAsync(Guid pipelineId, WorkKind kind = WorkKind.Capture, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind))
            return Result<RunModel>.Fail(AppErrorFactory.Validation("Unknown work kind."));
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EngineSql.ExecuteAsync(connection, transaction,
            "SELECT pg_advisory_xact_lock(hashtextextended('harvest:scheduler',0))", cancellationToken);
        if (await registry.GetPipelineAsync(pipelineId, cancellationToken) is not Result<PipelineModel>.Success { Value: var pipeline })
            return Result<RunModel>.Fail(RegistrationErrors.PipelineNotFound(pipelineId));
        if (await registry.GetSourceAsync(pipeline.SourceId, cancellationToken) is not Result<SourceModel>.Success { Value: var source })
            return Result<RunModel>.Fail(RegistrationErrors.SourceNotFound(pipeline.SourceId));
        Result<Guid> queued = await RunPlanner.QueueAsync(connection, transaction, pipeline, source, [.. adapters], kind,
            clock.GetUtcNow(), null, null, cancellationToken);
        if (queued is Result<Guid>.Failure failure)
            return Result<RunModel>.Fail(failure.Error);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(((Result<Guid>.Success)queued).Value, cancellationToken);
    }

    public async Task<Result<RunModel>> GetAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = EngineSql.Command(connection, null,
            $"SELECT {EngineSql.RunColumns} FROM harvest_runs WHERE id=@id", ("id", runId));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Result<RunModel>.Ok(EngineSql.Run(reader))
            : Result<RunModel>.Fail(AppErrorFactory.NotFound("The run was not found."));
    }

    public async Task<Result<IReadOnlyList<RunModel>>> ListAsync(Guid pipelineId, int limit = 50, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200)
            return Result<IReadOnlyList<RunModel>>.Fail(AppErrorFactory.Validation("Limit must be between 1 and 200."));
        if (await registry.GetPipelineAsync(pipelineId, cancellationToken) is not Result<PipelineModel>.Success)
            return Result<IReadOnlyList<RunModel>>.Fail(RegistrationErrors.PipelineNotFound(pipelineId));
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = EngineSql.Command(connection, null,
            $"SELECT {EngineSql.RunColumns} FROM harvest_runs WHERE pipeline_id=@id ORDER BY queued_at DESC,id DESC LIMIT @limit",
            ("id", pipelineId), ("limit", limit));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var runs = new List<RunModel>();
        while (await reader.ReadAsync(cancellationToken))
            runs.Add(EngineSql.Run(reader));
        return Result<IReadOnlyList<RunModel>>.Ok(runs);
    }

    public async Task<Result<RunModel>> CancelAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EngineSql.ExecuteAsync(connection, transaction,
            """
            UPDATE harvest_runs SET status='cancelled',cancellation_requested=true,finished_at=@now,completion_reason='cancelled'
            WHERE id=@id AND status IN ('queued','running','paused');
            UPDATE harvest_work_item SET state='blocked',lease_owner=NULL,lease_until=NULL,finished_at=@now,last_error='Run cancelled'
            WHERE run_id=@id AND state IN ('queued','running')
            """, cancellationToken, ("id", runId), ("now", clock.GetUtcNow()));
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(runId, cancellationToken);
    }

    public async Task<Result<PipelineModel>> SetPausedAsync(Guid pipelineId, bool paused, CancellationToken cancellationToken = default)
    {
        if (await registry.GetPipelineAsync(pipelineId, cancellationToken) is not Result<PipelineModel>.Success)
            return Result<PipelineModel>.Fail(RegistrationErrors.PipelineNotFound(pipelineId));
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        int changed = await EngineSql.ExecuteAsync(connection, null,
            """
            UPDATE harvest_pipelines p SET state=@state,pause_reason=@reason,state_since=@now
            FROM harvest_sources s WHERE p.id=@id AND s.id=p.source_id
              AND (p.state='active' OR (p.state='paused' AND p.pause_reason='operator'))
              AND (@paused OR (s.permission IN ('open','granted') AND s.state='active'))
            """, cancellationToken, ("id", pipelineId), ("paused", paused), ("state", paused ? "paused" : "active"),
            ("reason", paused ? "operator" : null), ("now", clock.GetUtcNow()));
        if (changed == 0)
            return Result<PipelineModel>.Fail(AppErrorFactory.Conflict("The pipeline cannot change state with its current source permission."));
        return await registry.GetPipelineAsync(pipelineId, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<HostHealthModel>>> HostsAsync(Guid containerId, CancellationToken cancellationToken = default)
    {
        if (await registry.GetContainerAsync(containerId, cancellationToken) is not Result<WoW.Two.Sdk.Beta.Be.Harvest.Containers.ContainerModel>.Success)
            return Result<IReadOnlyList<HostHealthModel>>.Fail(RegistrationErrors.ContainerNotFound(containerId));
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = EngineSql.Command(connection, null,
            """
            SELECT l.host_key,l.lane,l.paused_until,l.consecutive_failures,l.last_challenge_kind,
              count(*) FILTER(WHERE w.state='queued')::int,count(*) FILTER(WHERE w.state='running')::int
            FROM harvest_host_lane l JOIN harvest_work_item w ON w.host_key=l.host_key AND w.lane=l.lane
            WHERE w.container_id=@container GROUP BY l.host_key,l.lane ORDER BY l.host_key,l.lane
            """, ("container", containerId));
        var hosts = new List<HostHealthModel>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            hosts.Add(new HostHealthModel
            {
                HostKey = reader.GetString(0), Lane = reader.GetString(1),
                PausedUntilUtc = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                ConsecutiveFailures = reader.GetInt32(3), LastChallengeKind = reader.IsDBNull(4) ? null : reader.GetString(4),
                Queued = reader.GetInt32(5), Running = reader.GetInt32(6),
            });
        return Result<IReadOnlyList<HostHealthModel>>.Ok(hosts);
    }
}
