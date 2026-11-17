using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Reads and updates the staged source console over the shared Harvest schema.</summary>
public sealed class PipelineStages(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource database,
    IEnumerable<ISourceAdapter> adapters,
    IEnumerable<IStepBroker> brokers,
    TimeProvider clock, IServiceProvider services, IServiceProviderIsKeyedService keyedServices)
{
    private static readonly WorkKind[] Kinds = [WorkKind.Discover, WorkKind.Harvest, WorkKind.Process];

    /// <summary>Resolves one registered section by its instance configuration identity.</summary>
    public async Task<Guid?> FindSectionAsync(string? container, string? source, SourceFamily? family, string? sectionUrl,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(container) || string.IsNullOrWhiteSpace(source))
            return null;
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var command = EngineSql.Command(connection, null, """
            SELECT p.id FROM harvest_pipelines p JOIN harvest_sources s ON s.id=p.source_id
            JOIN harvest_containers c ON c.id=p.container_id
            WHERE c.external_ref=@container AND s.handle=@source AND (@family::text IS NULL OR s.family=@family)
              AND (p.scope->>'url') IS NOT DISTINCT FROM @url::text
            LIMIT 2
            """, ("container", container), ("source", source), ("url", sectionUrl),
            ("family", family is { } value ? SnakeCaseEnum<SourceFamily>.ToText(value) : null));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        Guid id = reader.GetGuid(0);
        return await reader.ReadAsync(ct) ? null : id;
    }

    /// <summary>Reads the three stages for an already authorized source section.</summary>
    public async Task<IReadOnlyList<PipelineStageModel>> ListAsync(PipelineModel pipeline, SourceModel source, CancellationToken ct)
    {
        var adapter = adapters.LastOrDefault(item => item.Serves(source));
        var descriptor = adapter?.Describe(source)
            ?? new AdapterDescriptor { Steps = [], Plans = new Dictionary<WorkKind, IReadOnlyList<string>>() };
        await using var connection = await database.OpenConnectionAsync(ct);
        var recent = new List<RunModel>();
        await using (var command = EngineSql.Command(connection, null, $"""
            SELECT recent.* FROM (VALUES ('discover'),('harvest'),('process')) stage(kind)
            CROSS JOIN LATERAL (
                SELECT {EngineSql.RunColumns} FROM harvest_runs WHERE pipeline_id=@pipeline AND kind=stage.kind
                ORDER BY queued_at DESC,id DESC LIMIT 5
            ) recent ORDER BY recent.queued_at DESC,recent.id DESC
            """, ("pipeline", pipeline.Id)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                recent.Add(EngineSql.Run(reader));
        }
        var pending = new Dictionary<WorkKind, long?>();
        await using (var command = EngineSql.Command(connection, null, """
            SELECT (SELECT count(*) FROM harvest_catalog_pages WHERE pipeline_id=@pipeline AND completed_at IS NULL),
                   (SELECT count(*) FROM harvest_discovery_scopes s JOIN harvest_discoveries d ON d.id=s.discovery_id
                    WHERE s.pipeline_id=@pipeline AND d.detail_state IN ('pending','failed'))
            """, ("pipeline", pipeline.Id)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            pending[WorkKind.Discover] = reader.GetInt64(0);
            pending[WorkKind.Harvest] = reader.GetInt64(1);
        }
        bool active = pipeline.State == PipelineState.Active && source.State == SourceState.Active
            && source.Permission is SourcePermission.Open or SourcePermission.Granted;
        var catalog = RunPlanner.DiscoverySection(adapter, source, pipeline.Scope);
        var processorKeys = CaptureProcessors.PublicKeys(services);
        return Kinds.Select(kind =>
        {
            var steps = RunPlanner.StageSteps(descriptor, kind);
            var options = RunPlanner.StageOptions(pipeline, kind)!;
            var processor = kind == WorkKind.Process ? CaptureProcessors.Resolve(services, options) : null;
            if (processor is not null)
                options = options with { Processor = processor.Key };
            return new PipelineStageModel(kind, active && steps.Count > 0 && (kind != WorkKind.Discover || catalog is not null),
                pipeline.Schedules.FirstOrDefault(schedule => schedule.Kind == kind), options,
                pending.GetValueOrDefault(kind), recent.Where(run => run.Kind == kind).ToArray())
            {
                Catalog = kind == WorkKind.Discover ? catalog : null,
                ProcessorKeys = kind == WorkKind.Process ? processorKeys : [],
                OperationKeys = PlanOf(kind, steps, adapter?.Key, processor),
            };
        }).ToArray();
    }

    private IReadOnlyList<string> PlanOf(WorkKind kind, IReadOnlyList<string> steps, string? adapterKey, ICaptureProcessor? processor)
    {
        if (kind == WorkKind.Process)
            return ProcessingStore.OperationKeys(processor);
        var operations = new List<string>();
        foreach (var step in steps)
        {
            var broker = brokers.LastOrDefault(row => row.AdapterKey == adapterKey && row.StepKey == step);
            operations.AddRange(broker?.OperationKeys is { Count: > 0 } keys ? keys : [step]);
        }
        if (operations.Count == 0)
            return operations;
        if (kind == WorkKind.Harvest)
            operations.AddRange(["normalize-capture", "save-capture"]);
        else
            operations.Add("save-discoveries");
        return operations;
    }

    /// <summary>Saves one stage without replacing any other schedule on its section.</summary>
    public async Task<Result<bool>> SaveAsync(PipelineModel pipeline, SourceModel source, ScheduleDeclaration schedule,
        bool seedOnly, CancellationToken ct)
    {
        if (!Kinds.Contains(schedule.Kind) || schedule.Stage is null)
            return Invalid("Select a stage and provide its settings.");
        if (RegistrationRules.ValidateSchedules([schedule], key => CaptureProcessors.IsRegistered(keyedServices, key)) is { } invalid)
            return Result<bool>.Fail(invalid);
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await EngineSql.ExecuteAsync(connection, transaction,
            "SELECT pg_advisory_xact_lock(hashtextextended('harvest:scheduler',0))", ct);
        var kind = SnakeCaseEnum<WorkKind>.ToText(schedule.Kind);
        if ((schedule.Kind is WorkKind.Discover or WorkKind.Harvest) && schedule.Enabled && schedule.Mode != ScheduleMode.Manual)
        {
            await using var command = EngineSql.Command(connection, transaction, """
                SELECT EXISTS (SELECT 1 FROM harvest_schedules
                           WHERE pipeline_id=@pipeline AND kind=@kind AND stage_options IS NOT NULL),
                       COALESCE(@disabled::boolean, (SELECT stage_options->>'disabled'='true'
                           FROM harvest_schedules WHERE pipeline_id=@pipeline AND kind=@kind), false),
                       EXISTS (SELECT 1 FROM harvest_schedules
                           WHERE pipeline_id=@pipeline AND kind='capture' AND enabled AND mode<>'manual')
                """, ("pipeline", pipeline.Id), ("kind", kind), ("disabled", schedule.Stage.Disabled));
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            if (seedOnly && reader.GetBoolean(0))
                return Result<bool>.Ok(false);
            if (!reader.GetBoolean(1))
            {
                // Startup seeds fill missing options without changing the section's chosen capture path.
                if (reader.GetBoolean(2))
                    return seedOnly ? Result<bool>.Ok(false) : Invalid(RegistrationRules.SchedulePathConflict);
                if (schedule.Kind == WorkKind.Discover
                    && RunPlanner.DiscoverySection(adapters.LastOrDefault(adapter => adapter.Serves(source)), source, pipeline.Scope) is null)
                    return Invalid("This pipeline has no published discovery catalog.");
            }
        }
        var now = clock.GetUtcNow();
        int changed = await EngineSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_schedules(id,container_id,pipeline_id,kind,mode,every_interval,max_interval,cron,
                time_zone,window_start,window_end,enabled,created_at,stage_options)
            VALUES(@id,@container,@pipeline,@kind,@mode,@every,@max,@cron,@zone,@start,@end,@enabled,@now,@stage::jsonb)
            ON CONFLICT(pipeline_id,kind) DO UPDATE SET
                mode=excluded.mode,every_interval=excluded.every_interval,max_interval=excluded.max_interval,cron=excluded.cron,
                time_zone=excluded.time_zone,window_start=excluded.window_start,window_end=excluded.window_end,
                enabled=excluded.enabled,
                stage_options=CASE WHEN excluded.stage_options->>'disabled' IS NULL
                    AND harvest_schedules.stage_options->>'disabled' IS NOT NULL
                    THEN jsonb_set(excluded.stage_options,'{disabled}',harvest_schedules.stage_options->'disabled')
                    ELSE excluded.stage_options END,
                next_due_at=CASE WHEN (harvest_schedules.mode,harvest_schedules.every_interval,harvest_schedules.max_interval,
                    harvest_schedules.cron,harvest_schedules.time_zone,harvest_schedules.window_start,harvest_schedules.window_end,
                    harvest_schedules.enabled, harvest_schedules.stage_options->>'startsOn',
                    coalesce(harvest_schedules.stage_options->>'disabled','false')) IS DISTINCT FROM
                    (excluded.mode,excluded.every_interval,excluded.max_interval,excluded.cron,excluded.time_zone,
                    excluded.window_start,excluded.window_end,excluded.enabled,excluded.stage_options->>'startsOn',
                    coalesce(excluded.stage_options->>'disabled',harvest_schedules.stage_options->>'disabled','false'))
                    THEN NULL ELSE harvest_schedules.next_due_at END
            WHERE NOT @seed OR harvest_schedules.stage_options IS NULL
            """, ct, ("id", Guid.CreateVersion7(now)), ("container", pipeline.ContainerId), ("pipeline", pipeline.Id),
            ("kind", kind), ("mode", SnakeCaseEnum<ScheduleMode>.ToText(schedule.Mode)),
            ("every", schedule.Every), ("max", schedule.MaxEvery), ("cron", schedule.Cron?.Trim()), ("zone", schedule.TimeZone),
            ("start", schedule.Window?.Start), ("end", schedule.Window?.End), ("enabled", schedule.Enabled), ("now", now),
            ("stage", StoreJson.Write(schedule.Stage)), ("seed", seedOnly));
        await transaction.CommitAsync(ct);
        return Result<bool>.Ok(changed > 0);
    }

    /// <summary>Lists one bounded page of catalog discoveries in an authorized section.</summary>
    public async Task<IReadOnlyList<PipelineDiscoveryModel>> DiscoveriesAsync(Guid pipelineId, string? state, int offset, int limit,
        CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var command = EngineSql.Command(connection, null, """
            SELECT d.id,d.external_key,d.url,d.title,d.published_at,s.last_seen_at,s.catalog_url,d.detail_state,
                d.next_detail_at,l.post_id,d.native_fields
            FROM harvest_discovery_scopes s JOIN harvest_discoveries d ON d.id=s.discovery_id
            LEFT JOIN harvest_post_links l ON l.id=d.link_id
            WHERE s.pipeline_id=@pipeline AND (@state::text IS NULL OR d.detail_state=@state)
            ORDER BY s.last_seen_at DESC,s.discovery_id LIMIT @limit OFFSET @offset
            """, ("pipeline", pipelineId), ("state", state), ("limit", limit), ("offset", offset));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<PipelineDiscoveryModel>();
        while (await reader.ReadAsync(ct))
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), String(reader, 3), Date(reader, 4),
                reader.GetFieldValue<DateTimeOffset>(5), reader.GetString(6), reader.GetString(7), reader.GetFieldValue<DateTimeOffset>(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.IsDBNull(10) ? null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(10))));
        return rows;
    }

    /// <summary>Lists the claims of an authorized run, with the operations inside each attempt.</summary>
    public async Task<IReadOnlyList<RunAttemptModel>> AuditAsync(Guid runId, int offset, int limit, CancellationToken ct,
        bool newestFirst = false)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        string ordering = newestFirst ? "a.started_at DESC,a.work_item_id DESC,a.attempt DESC"
            : "a.started_at,a.work_item_id,a.attempt";
        await using var command = EngineSql.Command(connection, null, $"""
            SELECT a.work_item_id,w.step,COALESCE(w.payload->>'url',w.target),a.attempt,a.started_at,a.finished_at,
                a.outcome,a.message,a.retry_at,a.http_status,a.discovered,a.operations
            FROM harvest_step_attempts a JOIN harvest_work_item w ON w.id=a.work_item_id
            WHERE a.run_id=@run ORDER BY {ordering} LIMIT @limit OFFSET @offset
            """, ("run", runId), ("limit", limit), ("offset", offset));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<RunAttemptModel>();
        while (await reader.ReadAsync(ct))
        {
            var finished = Date(reader, 5);
            var operations = JsonSerializer.Deserialize<RunOperationModel[]>(reader.GetString(11), JsonOptionsConstants.Default) ?? [];
            if (finished is { } end)
                for (int i = 0; i < operations.Length; i++)
                {
                    var operation = operations[i];
                    if (operation.FinishedAtUtc is null)
                        operations[i] = operation with
                        {
                            FinishedAtUtc = end, Result = new("interrupted", Message: "The attempt ended during this operation."),
                        };
                }
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetFieldValue<DateTimeOffset>(4), finished, String(reader, 6), String(reader, 7), Date(reader, 8),
                reader.IsDBNull(9) ? null : reader.GetInt16(9), reader.GetInt32(10)) { Operations = operations });
        }
        return rows;
    }

    private static Result<bool> Invalid(string message) => Result<bool>.Fail(AppErrorFactory.Validation(message));
    private static DateTimeOffset? Date(NpgsqlDataReader reader, int index) => reader.IsDBNull(index)
        ? null : reader.GetFieldValue<DateTimeOffset>(index);
    private static string? String(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
}
