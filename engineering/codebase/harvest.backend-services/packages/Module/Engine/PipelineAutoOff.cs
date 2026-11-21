using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>The pipeline's own threshold, and the turn-off of its harvesting schedules once that many capture, check, refresh,
/// discover or harvest runs in a row have failed. A pipeline's state, its process rows and runs started by hand stay as they are.</summary>
/// <remarks>The failure count is read from <c>harvest_runs</c>, never stored: it is the leading <c>failed</c> verdicts of the pipeline's
/// harvesting runs, newest first, among the runs queued at or after <c>harvest_pipelines.failures_since</c>. Nothing here is written
/// inside a run's completion transaction.</remarks>
public sealed class PipelineAutoOff(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource database,
    IOptions<HarvestEngineOptions> options,
    TimeProvider clock,
    ILogger<PipelineAutoOff> logger)
{
    /// <summary>Holds the largest failed-run count a pipeline or the host may set.</summary>
    public const int MaxAfter = 1000;

    /// <summary>Holds the work kinds that count and turn off, as the SQL list the verb's five kinds spell.</summary>
    internal const string Kinds = "'capture','check','refresh','discover','harvest'";

    /// <summary>Holds one run's verdict over a <c>harvest_runs</c> row aliased <c>r</c>: <c>failed</c> when it saved nothing and was
    /// refused, errored or failed at queue time, <c>reset</c> when it saved anything or completed, <c>skip</c> when it says nothing.</summary>
    internal const string Verdict =
        """
        CASE
          WHEN r.kind NOT IN ('capture','check','refresh','discover','harvest')
            OR r.status NOT IN ('completed','partial','failed')
            OR (r.status = 'completed' AND r.completion_reason = 'no_items') THEN 'skip'
          WHEN r.status = 'completed'
            OR r.captured + r.unchanged + CASE WHEN r.kind = 'discover' THEN r.discovered ELSE 0 END > 0 THEN 'reset'
          WHEN r.status = 'failed' OR r.failed > 0 THEN 'failed'
          ELSE 'skip'
        END
        """;

    /// <summary>Tells whether a schedule kind is one of the five that count and turn off; <c>process</c> never is.</summary>
    /// <param name="kind">The schedule's work kind.</param>
    /// <returns>True for capture, check, refresh, discover and harvest.</returns>
    internal static bool IsHarvesting(WorkKind kind) =>
        kind is WorkKind.Capture or WorkKind.Check or WorkKind.Refresh or WorkKind.Discover or WorkKind.Harvest;

    private const string SchedulerLock = "SELECT pg_advisory_xact_lock(hashtextextended('harvest:scheduler',0))";

    /// <summary>Turns the pipeline's harvesting schedules off when its newest runs reach the count. The caller holds the scheduler lock.</summary>
    /// <param name="connection">The caller's open connection.</param>
    /// <param name="transaction">The caller's transaction, which holds the scheduler lock.</param>
    /// <param name="pipelineId">The pipeline.</param>
    /// <param name="now">The current time.</param>
    /// <param name="ct">The token that cancels the call.</param>
    /// <returns>Whether this call turned schedules off and wrote the record.</returns>
    internal async Task<bool> TurnOffIfDueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid pipelineId,
        DateTimeOffset now, CancellationToken ct)
    {
        int? own;
        DateTimeOffset? since;
        await using (NpgsqlCommand lockRow = EngineSql.Command(connection, transaction,
            "SELECT auto_off_after,failures_since FROM harvest_pipelines WHERE id=@pipeline FOR UPDATE", ("pipeline", pipelineId)))
        {
            await using NpgsqlDataReader reader = await lockRow.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return false;
            own = reader.IsDBNull(0) ? null : reader.GetInt32(0);
            since = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
        }
        int after = own ?? options.Value.AutoOffAfter;
        if (after <= 0)
            return false;

        Guid newest = Guid.Empty;
        string reason = "";
        int seen = 0;
        await using (NpgsqlCommand runs = EngineSql.Command(connection, transaction,
            $"""
            SELECT v.id,v.verdict,COALESCE(v.fetch_outcome,v.status) FROM (
                SELECT r.id,r.queued_at,r.fetch_outcome,r.status,{Verdict} AS verdict
                FROM harvest_runs r WHERE r.pipeline_id=@pipeline AND (@since::timestamptz IS NULL OR r.queued_at>=@since)
            ) v WHERE v.verdict<>'skip' ORDER BY v.queued_at DESC,v.id DESC LIMIT @after
            """, ("pipeline", pipelineId), ("since", since), ("after", after)))
        {
            await using NpgsqlDataReader reader = await runs.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.GetString(1) != "failed")
                    return false;
                if (seen++ == 0)
                {
                    newest = reader.GetGuid(0);
                    reason = reader.GetString(2);
                }
            }
        }
        if (seen != after)
            return false;

        var kinds = new List<string>();
        await using (NpgsqlCommand off = EngineSql.Command(connection, transaction,
            $"""
            UPDATE harvest_schedules SET enabled=false,next_due_at=NULL
            WHERE pipeline_id=@pipeline AND kind IN ({Kinds}) AND enabled AND mode<>'manual' RETURNING kind
            """, ("pipeline", pipelineId)))
        {
            await using NpgsqlDataReader reader = await off.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                kinds.Add(reader.GetString(0));
        }
        if (kinds.Count == 0)
            return false;

        await EngineSql.ExecuteAsync(connection, transaction,
            """
            UPDATE harvest_pipelines SET auto_off_at=@now,auto_off_run_id=@run,auto_off_failures=@failures,auto_off_reason=@reason
            WHERE id=@pipeline
            """, ct, ("now", now), ("run", newest), ("failures", after), ("reason", reason), ("pipeline", pipelineId));
        logger.LogWarning(
            "Harvest pipeline {PipelineId} turned its {Kinds} schedules off after {Failures} failed runs in a row; run {RunId} ended {Reason}",
            pipelineId, string.Join(",", kinds.Order(StringComparer.Ordinal)), after, newest, reason);
        return true;
    }

    /// <summary>Checks a pipeline after one of its runs ended failed, in its own transaction.</summary>
    /// <param name="pipelineId">The pipeline the run belongs to.</param>
    /// <param name="ct">The token that cancels the call.</param>
    /// <returns>A task that completes when the check is committed.</returns>
    internal async Task AfterFailedRunAsync(Guid pipelineId, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);
        // The scheduler's gate comes first, as in the stage save and the pipeline update, and nothing is held before it.
        await EngineSql.ExecuteAsync(connection, transaction, SchedulerLock, ct);
        await TurnOffIfDueAsync(connection, transaction, pipelineId, clock.GetUtcNow(), ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>Starts the failure count again and clears the turn-off record, in the caller's transaction.</summary>
    /// <param name="connection">The caller's open connection.</param>
    /// <param name="transaction">The caller's transaction, which holds the scheduler lock.</param>
    /// <param name="pipelineId">The pipeline whose schedule was turned back on.</param>
    /// <param name="now">The current time: runs queued before it never count.</param>
    /// <param name="ct">The token that cancels the call.</param>
    /// <returns>A task that completes when the row is written.</returns>
    internal static Task ResumeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid pipelineId, DateTimeOffset now,
        CancellationToken ct) =>
        EngineSql.ExecuteAsync(connection, transaction,
            """
            UPDATE harvest_pipelines SET failures_since=@now,auto_off_at=NULL,auto_off_run_id=NULL,auto_off_failures=NULL,auto_off_reason=NULL
            WHERE id=@pipeline
            """, ct, ("now", now), ("pipeline", pipelineId));

    /// <summary>Reads the pipeline's threshold, its failed runs in a row so far and the turn-off record.</summary>
    /// <param name="pipelineId">The pipeline.</param>
    /// <param name="ct">The token that cancels the call.</param>
    /// <returns>The state, or NotFound for an unknown pipeline.</returns>
    public async Task<Result<PipelineAutoOffState>> ReadAsync(Guid pipelineId, CancellationToken ct = default)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(ct);
        int? own;
        DateTimeOffset? since;
        PipelineAutoOffModel? record = null;
        await using (NpgsqlCommand row = EngineSql.Command(connection, null,
            """
            SELECT auto_off_after,failures_since,auto_off_at,auto_off_run_id,auto_off_failures,auto_off_reason
            FROM harvest_pipelines WHERE id=@pipeline
            """, ("pipeline", pipelineId)))
        {
            await using NpgsqlDataReader reader = await row.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return Result<PipelineAutoOffState>.Fail(RegistrationErrors.PipelineNotFound(pipelineId));
            own = reader.IsDBNull(0) ? null : reader.GetInt32(0);
            since = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
            if (!reader.IsDBNull(2) && !reader.IsDBNull(3))
                record = new PipelineAutoOffModel
                {
                    AtUtc = reader.GetFieldValue<DateTimeOffset>(2), RunId = reader.GetGuid(3),
                    Failures = reader.IsDBNull(4) ? 0 : reader.GetInt32(4), Reason = reader.IsDBNull(5) ? "failed" : reader.GetString(5),
                };
        }

        int failures = 0;
        await using (NpgsqlCommand runs = EngineSql.Command(connection, null,
            $"""
            SELECT v.verdict FROM (
                SELECT r.queued_at,r.id,{Verdict} AS verdict
                FROM harvest_runs r WHERE r.pipeline_id=@pipeline AND (@since::timestamptz IS NULL OR r.queued_at>=@since)
            ) v WHERE v.verdict<>'skip' ORDER BY v.queued_at DESC,v.id DESC LIMIT {MaxAfter}
            """, ("pipeline", pipelineId), ("since", since)))
        {
            await using NpgsqlDataReader reader = await runs.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct) && reader.GetString(0) == "failed")
                failures++;
        }
        return Result<PipelineAutoOffState>.Ok(new(own ?? options.Value.AutoOffAfter, own is null, failures, record));
    }

    /// <summary>Sets the pipeline's own threshold. It writes only that value: it never turns a schedule on or off and never starts the count again.</summary>
    /// <param name="pipelineId">The pipeline.</param>
    /// <param name="after">The failed runs in a row that turn it off, 0–1000, where <c>0</c> never does; null returns to the host's value.</param>
    /// <param name="ct">The token that cancels the call.</param>
    /// <returns>The state after the write, a Validation error for a value outside 0–1000, or NotFound for an unknown pipeline.</returns>
    public async Task<Result<PipelineAutoOffState>> SetAfterAsync(Guid pipelineId, int? after, CancellationToken ct = default)
    {
        if (after is < 0 or > MaxAfter)
            return Result<PipelineAutoOffState>.Fail(AppErrorFactory.Validation($"The count must be between 0 and {MaxAfter}."));
        await using (NpgsqlConnection connection = await database.OpenConnectionAsync(ct))
        {
            if (await EngineSql.ExecuteAsync(connection, null, "UPDATE harvest_pipelines SET auto_off_after=@after WHERE id=@pipeline", ct,
                ("after", after), ("pipeline", pipelineId)) == 0)
                return Result<PipelineAutoOffState>.Fail(RegistrationErrors.PipelineNotFound(pipelineId));
        }
        return await ReadAsync(pipelineId, ct);
    }
}

/// <summary>Holds a pipeline's failed-run threshold, its failed runs in a row so far and the turn-off in force.</summary>
/// <param name="After">The failed runs in a row that turn its harvesting schedules off; <c>0</c> never does.</param>
/// <param name="FromDefault">Whether <paramref name="After"/> is the host's value, because the pipeline has none of its own.</param>
/// <param name="Failures">The failed runs in a row so far.</param>
/// <param name="Off">The turn-off in force, or null when there is none.</param>
public sealed record PipelineAutoOffState(int After, bool FromDefault, int Failures, PipelineAutoOffModel? Off);
