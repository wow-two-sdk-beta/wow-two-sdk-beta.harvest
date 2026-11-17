using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Rendering;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Schedules runs and processes recoverable, fenced work claims.</summary>
public sealed class HarvestEngine(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource database,
    IRegistrationBroker registry,
    IEnumerable<ISourceAdapter> adapters,
    IEnumerable<IStepBroker> brokers,
    IFetchClientFactory clients,
    IChallengeDetector challenges,
    IEnumerable<IBrowserRenderer> renderers,
    IPostExtractor extractor,
    IPostBroker posts,
    IServiceProvider services,
    IOptions<HarvestEngineOptions> options,
    TimeProvider clock,
    ILogger<HarvestEngine> logger)
{
    /// <summary>Coalesces missed schedule ticks into one current execution per pipeline and work kind.</summary>
    public async Task<int> QueueDueAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = clock.GetUtcNow();
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (NpgsqlCommand gate = EngineSql.Command(connection, transaction,
            "SELECT pg_try_advisory_xact_lock(hashtextextended('harvest:scheduler',0))"))
        {
            if (await gate.ExecuteScalarAsync(cancellationToken) is not true)
                return 0;
        }

        var candidates = new List<(Guid Id, Guid PipelineId, DateTimeOffset? Due)>();
        await using (NpgsqlCommand query = EngineSql.Command(connection, transaction,
            """
            SELECT s.id,s.pipeline_id,s.next_due_at FROM harvest_schedules s
            JOIN harvest_pipelines p ON p.id=s.pipeline_id JOIN harvest_sources src ON src.id=p.source_id
            WHERE s.enabled AND s.mode<>'manual' AND p.state='active' AND src.state='active'
              AND s.stage_options->>'disabled' IS DISTINCT FROM 'true'
              AND (s.kind<>'capture' OR NOT EXISTS (
                  SELECT 1 FROM harvest_schedules staged WHERE staged.pipeline_id=s.pipeline_id
                    AND staged.kind IN ('discover','harvest') AND staged.enabled AND staged.mode<>'manual'
                    AND staged.stage_options->>'disabled' IS DISTINCT FROM 'true'))
              AND src.permission IN ('open','granted') AND (s.next_due_at IS NULL OR s.next_due_at<=@now)
            ORDER BY s.next_due_at NULLS FIRST,s.id LIMIT 100
            FOR UPDATE OF s SKIP LOCKED
            """, ("now", now)))
        {
            await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                candidates.Add((reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2)));
        }

        int queued = 0;
        foreach ((Guid id, Guid pipelineId, DateTimeOffset? storedDue) in candidates)
        {
            if (await registry.GetPipelineAsync(pipelineId, cancellationToken) is not Result<PipelineModel>.Success { Value: var pipeline }
                || await registry.GetSourceAsync(pipeline.SourceId, cancellationToken) is not Result<SourceModel>.Success { Value: var source })
                continue;
            ScheduleModel schedule = pipeline.Schedules.Single(row => row.Id == id);
            DateTimeOffset? due;
            DateTimeOffset? next;
            try
            {
                due = storedDue ?? ScheduleCalculator.Next(schedule, now, first: true);
                next = due <= now ? ScheduleCalculator.Next(schedule, now) : due;
            }
            catch (Exception error) when (error is ArgumentException or Cronos.CronFormatException or TimeZoneNotFoundException)
            {
                logger.LogWarning(error, "Disabling invalid Harvest schedule {ScheduleId}", id);
                await EngineSql.ExecuteAsync(connection, transaction,
                    "UPDATE harvest_schedules SET enabled=false WHERE id=@id", cancellationToken, ("id", id));
                continue;
            }
            if (due <= now)
            {
                Result<Guid> result = await RunPlanner.QueueAsync(connection, transaction, pipeline, source, [.. adapters],
                    schedule.Kind, now, id, due, cancellationToken);
                if (result is Result<Guid>.Success)
                    queued++;
            }
            await EngineSql.ExecuteAsync(connection, transaction,
                "UPDATE harvest_schedules SET next_due_at=@next,last_queued_at=CASE WHEN @ran THEN @now ELSE last_queued_at END WHERE id=@id",
                cancellationToken, ("id", id), ("next", next), ("ran", due <= now), ("now", now));
        }
        await transaction.CommitAsync(cancellationToken);
        return queued;
    }

    /// <summary>Executes one due item, or returns false when no host lane can currently work.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        WorkClaim? claim = await ClaimAsync(cancellationToken);
        if (claim is null)
            return false;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(options.Value.LeaseDuration - TimeSpan.FromSeconds(5));
        var trace = new StepTrace(database, claim.Item, claim.Owner, clock, logger, cancellationToken);
        if (claim.Frozen.Kind == WorkKind.Process)
        {
            await ProcessSavedCaptureAsync(claim, trace, lifetime.Token, cancellationToken);
            return true;
        }
        using HttpClient http = clients.CreateClient();
        var context = new EngineStepContext(claim.Plan, claim.Frozen.Source, http, challenges, clock,
            renderers.FirstOrDefault(), claim.Frozen.Pipeline.Scope, trace);
        StepResult result;
        var readings = new List<(Capture Capture, PostEnvelope Envelope)>();
        try
        {
            IStepBroker? broker = brokers.LastOrDefault(candidate => candidate.AdapterKey == claim.Frozen.AdapterKey
                && candidate.StepKey == claim.Item.StepKey);
            if (claim.Plan.Requirements.Render == RenderNeed.Required && context.Browser is null)
                result = new StepResult { Outcome = FetchOutcome.CapabilityUnavailable, Message = "No browser renderer is registered." };
            else if (broker is null)
                result = new StepResult { Outcome = FetchOutcome.CapabilityUnavailable, Message = "No broker is registered for the frozen step." };
            else if (broker.OperationKeys.Count > 0)
                result = await broker.ExecuteAsync(claim.Item, context, lifetime.Token);
            else
                result = await trace.RunAsync(claim.Item.StepKey, () => broker.ExecuteAsync(claim.Item, context, lifetime.Token),
                    value => new(value.Outcome is FetchOutcome.Ok or FetchOutcome.Gone ? "completed" : "failed",
                        Input: 1, Output: value.Captures.Count, Message: value.Outcome.ToString()));
            if (result.Outcome == FetchOutcome.RenderRequired && context.Browser is null)
                result = result with { Outcome = FetchOutcome.CapabilityUnavailable, Message = "This page needs the unbound browser renderer." };
            StepSignal? refusal = context.Signals.LastOrDefault(signal => IsRefusal(signal.Outcome));
            if (refusal is not null)
                result = result with { Outcome = refusal.Outcome, Challenge = refusal.Challenge, RetryAfter = refusal.RetryAfter, Captures = [], Next = [], Links = [] };

            if (result.Outcome == FetchOutcome.Ok && result.Captures.Count > 0)
            {
                var captures = claim.Frozen.Kind == WorkKind.Discover ? result.Captures
                    : result.Captures.Take(claim.Frozen.Pipeline.Limits.MaxItems ?? options.Value.MaxItems).ToArray();
                await trace.RunAsync("normalize-capture", async () =>
                {
                    foreach (Capture capture in captures)
                    {
                        Result<PostEnvelope> extraction = await extractor.ExtractAsync(capture, new ExtractionRequest
                        {
                            Family = claim.Frozen.Source.Family,
                            Ai = AiMode.Off,
                            AiAllowed = false,
                            BudgetAllowsAi = false,
                        }, lifetime.Token);
                        if (extraction is Result<PostEnvelope>.Success success)
                            readings.Add((capture, success.Value));
                        else
                            throw new InvalidOperationException(((Result<PostEnvelope>.Failure)extraction).Error.Message);
                    }
                    return readings.Count;
                }, count => new(Input: captures.Count, Output: count));
            }
            if (claim.Frozen.Kind == WorkKind.Harvest && result.Outcome == FetchOutcome.Ok && readings.Count == 0)
                result = result with { Outcome = FetchOutcome.UnexpectedContent, Message = "The listing did not yield a capture." };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            result = new StepResult { Outcome = FetchOutcome.FetchTimeout, Message = "The step exceeded its claim time budget." };
        }
        catch (Exception error)
        {
            logger.LogError(error, "Harvest step {Step} failed in run {RunId}", claim.Item.StepKey, claim.Item.RunId);
            result = new StepResult { Outcome = FetchOutcome.UnexpectedContent, Message = "The step could not read its content. See the host log." };
        }
        int? httpStatus = result.Outcome is FetchOutcome.NetworkError or FetchOutcome.FetchTimeout
            ? null : context.Signals.LastOrDefault()?.HttpStatus;
        await CompleteTracedAsync(claim, result, readings, null, httpStatus, trace, cancellationToken);
        return true;
    }

    private async Task ProcessSavedCaptureAsync(WorkClaim claim, StepTrace trace, CancellationToken stepToken, CancellationToken ct)
    {
        CaptureProcessResult processed;
        try
        {
            processed = await ProcessingStore.ProcessAsync(database, services, claim.Frozen, claim.Item, trace, stepToken);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            processed = new(new StepResult { Outcome = FetchOutcome.FetchTimeout, Message = "Processing exceeded the claim time budget." });
        }
        catch (Exception error)
        {
            logger.LogWarning("Saved capture processing failed in run {RunId} with {ErrorType}", claim.Item.RunId, error.GetType().Name);
            processed = new(new StepResult { Outcome = FetchOutcome.UnexpectedContent, Message = "The processing result could not be read." });
        }
        await CompleteTracedAsync(claim, processed.StepResult, [], processed.Output, null, trace, ct);
    }

    private async Task CompleteTracedAsync(WorkClaim claim, StepResult result,
        IReadOnlyList<(Capture Capture, PostEnvelope Envelope)> readings, JsonElement? processingOutput, int? httpStatus,
        StepTrace trace, CancellationToken ct)
    {
        string? operation = result.Outcome is FetchOutcome.Ok or FetchOutcome.Gone ? claim.Frozen.Kind switch
        {
            WorkKind.Discover => "save-discoveries",
            WorkKind.Harvest => "save-capture",
            WorkKind.Process => processingOutput is not null ? "save-result" : null,
            _ => readings.Count > 0 ? "save-capture" : null,
        } : null;
        int? index = operation is null ? null : await trace.BeginAsync(operation);
        try
        {
            await CompleteAsync(claim, result, readings, processingOutput, httpStatus, trace, index, ct);
        }
        catch (Exception error) when (index.HasValue && !ct.IsCancellationRequested)
        {
            trace.End(index.Value, StepTrace.Failure(error));
            await trace.PublishAsync();
            throw;
        }
    }

    private async Task<WorkClaim?> ClaimAsync(CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);
        await using NpgsqlCommand command = EngineSql.Command(connection, transaction,
            """
            SELECT w.id,w.run_id,w.pipeline_id,w.container_id,w.step,w.lane,w.host_key,w.target,w.payload,w.attempts,w.plan_ref,r.frozen
            FROM harvest_work_item w JOIN harvest_runs r ON r.id=w.run_id
            JOIN harvest_pipelines p ON p.id=w.pipeline_id JOIN harvest_sources s ON s.id=p.source_id
            JOIN harvest_host_lane h ON h.host_key=w.host_key AND h.lane=w.lane
            JOIN harvest_host_state hs ON hs.host_key=w.host_key
            WHERE (w.state='queued' OR (w.state='running' AND w.lease_until<=@now))
              AND w.due_at<=@now AND w.not_before<=@now AND r.status IN ('queued','running')
              AND NOT r.cancellation_requested AND p.state='active' AND s.state='active' AND s.permission IN ('open','granted')
              AND (h.paused_until IS NULL OR h.paused_until<=@now)
              AND (r.kind='process' OR COALESCE(GREATEST(hs.paused_until,hs.retry_after_until),@now)<=@now)
              AND NOT EXISTS(SELECT 1 FROM harvest_work_item busy WHERE busy.host_key=w.host_key AND busy.lane=w.lane
                AND busy.state='running' AND busy.lease_until>@now)
            ORDER BY w.priority DESC,w.due_at,w.id LIMIT 1 FOR UPDATE OF r,w,h SKIP LOCKED
            """, ("now", now));
        WorkClaim? claim;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                return null;
            FrozenRun frozen = JsonSerializer.Deserialize<FrozenRun>(reader.GetString(11), JsonOptionsConstants.Default)!;
            var item = new StepItem
            {
                WorkItemId = reader.GetGuid(0), RunId = reader.GetGuid(1), PipelineId = reader.GetGuid(2), ContainerId = reader.GetGuid(3),
                SourceId = frozen.Source.Id, StepKey = reader.GetString(4), Lane = reader.GetString(5), HostKey = reader.GetString(6),
                Target = reader.GetString(7), Payload = reader.IsDBNull(8) ? null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(8)),
                Attempt = reader.GetInt32(9),
            };
            ResolvedPlan plan = JsonSerializer.Deserialize<ResolvedPlan>(reader.GetString(10), JsonOptionsConstants.Default)!;
            claim = new WorkClaim(item, frozen, plan, Guid.NewGuid().ToString("N"));
        }
        await EngineSql.ExecuteAsync(connection, transaction,
            """
            UPDATE harvest_work_item SET state='running',attempts=attempts+1,lease_owner=@owner,lease_until=@until WHERE id=@id;
            UPDATE harvest_runs SET status='running',started_at=coalesce(started_at,@now),stage=@step WHERE id=@run;
            UPDATE harvest_step_attempts SET finished_at=@now,outcome='interrupted',message='Worker lease expired.'
              WHERE work_item_id=@id AND finished_at IS NULL;
            INSERT INTO harvest_step_attempts(run_id,work_item_id,attempt,started_at) VALUES(@run,@id,@attempt,@now)
            """, ct, ("id", claim.Item.WorkItemId), ("owner", claim.Owner), ("until", now + options.Value.LeaseDuration),
            ("run", claim.Item.RunId), ("now", now), ("step", claim.Item.StepKey), ("attempt", claim.Item.Attempt+1));
        await transaction.CommitAsync(ct);
        return claim;
    }

    private async Task CompleteAsync(
        WorkClaim claim,
        StepResult result,
        IReadOnlyList<(Capture Capture, PostEnvelope Envelope)> readings,
        JsonElement? processingOutput,
        int? httpStatus,
        StepTrace trace,
        int? storeOperation,
        CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);
        // Lock the run before its claim, matching cancellation's lock order. A cancelled or expired result writes nothing.
        await using (NpgsqlCommand fence = EngineSql.Command(connection, transaction,
            """
            SELECT r.id FROM harvest_runs r WHERE r.id=@run AND r.status IN ('queued','running')
              AND NOT r.cancellation_requested FOR UPDATE
            """, ("run", claim.Item.RunId)))
        {
            if (await fence.ExecuteScalarAsync(ct) is not Guid)
                return;
        }
        await using (NpgsqlCommand fence = EngineSql.Command(connection, transaction,
            """
            SELECT id FROM harvest_work_item WHERE id=@id AND state='running' AND lease_owner=@owner
              AND lease_until>@now FOR UPDATE
            """, ("id", claim.Item.WorkItemId), ("owner", claim.Owner), ("now", now)))
        {
            if (await fence.ExecuteScalarAsync(ct) is not Guid)
                return;
        }

        RunReason? limit = await LimitAsync(connection, transaction, claim, now, ct);
        bool withinLimits = limit is null;
        int captured = 0;
        int created = 0;
        int unchanged = 0;
        Guid? detailLink = null;
        var savedKeys = new HashSet<string>(StringComparer.Ordinal);
        if (limit is null && result.Outcome == FetchOutcome.Ok)
        {
            await using NpgsqlCommand count = EngineSql.Command(connection, transaction,
                "SELECT captured+unchanged FROM harvest_runs WHERE id=@run", ("run", claim.Item.RunId));
            int used = (int)(await count.ExecuteScalarAsync(ct))!;
            int remaining = claim.Frozen.Kind == WorkKind.Discover ? readings.Count
                : Math.Max(0, (claim.Frozen.Pipeline.Limits.MaxItems ?? options.Value.MaxItems) - used);
            foreach ((Capture capture, PostEnvelope envelope) in readings.Take(remaining))
            {
                Result<CaptureSaveResult> saved = await posts.SaveCaptureAsync(claim.Item.ContainerId, claim.Item.SourceId,
                    claim.Item.RunId, capture, envelope, ct);
                if (saved is Result<CaptureSaveResult>.Failure failure)
                    throw new InvalidOperationException(failure.Error.Message);
                var captureResult = ((Result<CaptureSaveResult>.Success)saved).Value;
                detailLink ??= captureResult.LinkId;
                if (captureResult.Created)
                    created++;
                if (captureResult.Changed)
                    captured++;
                else
                    unchanged++;
                savedKeys.Add(capture.ExternalKey);
            }
            if (readings.Count > remaining || result.Captures.Count > readings.Count)
                limit = RunReason.ItemLimit;
        }
        if (result.Outcome is FetchOutcome.Ok or FetchOutcome.Gone)
            foreach (LinkObservation observation in result.Links)
            {
                if (result.Captures.Count > 0 && !savedKeys.Contains(observation.ExternalKey))
                    continue;
                if (result.Outcome == FetchOutcome.Gone && (claim.Item.Payload is null || claim.Frozen.Kind == WorkKind.Harvest))
                    continue;
                Result<PostModel> observed = await posts.ObserveLinkAsync(claim.Item.ContainerId, claim.Item.SourceId,
                    observation, claim.Item.RunId, ct);
                if (observed is Result<PostModel>.Failure failure)
                    throw new InvalidOperationException(failure.Error.Message);
            }

        int discovered = 0;
        int savedResults = 0;
        if (claim.Frozen.Kind == WorkKind.Harvest && limit is null)
            await DiscoveryStore.CompleteDetailAsync(connection, transaction, claim.Item, result.Outcome, detailLink,
                now, result.RetryAfter is { } detailWait ? now + detailWait : null, ct);

        if (claim.Frozen.Kind == WorkKind.Process)
        {
            savedResults = await ProcessingStore.CompleteAsync(connection, transaction, claim.Frozen, claim.Item, processingOutput, now, ct);
            if (processingOutput is not null)
                captured++;
        }
        else if (claim.Frozen.Kind == WorkKind.Discover && limit is null && result.Outcome is FetchOutcome.Ok or FetchOutcome.Gone)
        {
            var (found, pageLimit) = await DiscoveryStore.CompleteCatalogAsync(connection, transaction, claim.Frozen,
                claim.Item, result.Next, now, ct);
            discovered = found;
            if (pageLimit)
                await EngineSql.ExecuteAsync(connection, transaction,
                    "UPDATE harvest_runs SET completion_reason=COALESCE(completion_reason,'page_limit'),enumeration_complete=false WHERE id=@id",
                    ct, ("id", claim.Item.RunId));
        }
        else if (limit is null && result.Outcome == FetchOutcome.Ok && claim.Frozen.Kind != WorkKind.Harvest)
        {
            int max = (claim.Frozen.Pipeline.Limits.MaxItems ?? options.Value.MaxItems)
                + (claim.Frozen.Pipeline.Limits.MaxPages ?? 100);
            await using NpgsqlCommand count = EngineSql.Command(connection, transaction,
                "SELECT count(*)::int FROM harvest_work_item WHERE run_id=@run", ("run", claim.Item.RunId));
            int existing = (int)(await count.ExecuteScalarAsync(ct))!;
            foreach (StepTarget target in result.Next)
            {
                if (existing >= max)
                {
                    await EngineSql.ExecuteAsync(connection, transaction,
                        "UPDATE harvest_runs SET completion_reason='item_limit',enumeration_complete=false WHERE id=@id", ct,
                        ("id", claim.Item.RunId));
                    break;
                }
                RunPlanner.EnqueueResult queued = await RunPlanner.EnqueueAsync(connection, transaction, claim.Frozen,
                    claim.Item.RunId, target, now, ct);
                if (queued == RunPlanner.EnqueueResult.PageLimit)
                    await EngineSql.ExecuteAsync(connection, transaction,
                        "UPDATE harvest_runs SET completion_reason=coalesce(completion_reason,'page_limit'),enumeration_complete=false WHERE id=@id",
                        ct, ("id", claim.Item.RunId));
                if (queued == RunPlanner.EnqueueResult.Enqueued)
                {
                    existing++;
                    discovered++;
                }
            }
        }

        bool ok = result.Outcome is FetchOutcome.Ok or FetchOutcome.Gone;
        bool refused = IsRefusal(result.Outcome);
        bool stopForHost = refused || result.Outcome == FetchOutcome.NetworkError;
        if (refused)
            await BackoffAsync(connection, transaction, claim, result, now, ct);
        if (storeOperation is int index)
        {
            var measurement = claim.Frozen.Kind switch
            {
                WorkKind.Discover => new StepOperationResult(Input: result.Next.Count, Output: discovered),
                WorkKind.Process => new StepOperationResult(Input: 1, Output: savedResults),
                _ => new StepOperationResult(Input: readings.Count, Output: captured + unchanged,
                    Message: result.Outcome is FetchOutcome.Gone ? "Marked the detail as gone." : null),
            };
            trace.End(index, withinLimits || claim.Frozen.Kind == WorkKind.Process
                ? measurement : new("skipped", Message: "The run reached its limit before saving."));
        }
        await EngineSql.ExecuteAsync(connection, transaction,
            """
            UPDATE harvest_work_item SET state=@state,outcome=@outcome,last_error=@message,finished_at=@now,
              lease_owner=NULL,lease_until=NULL WHERE id=@id;
            UPDATE harvest_step_attempts SET finished_at=@now,outcome=@outcome,message=@message,
              retry_at=GREATEST(@retry::timestamptz,(
                SELECT GREATEST(s.paused_until,s.retry_after_until,l.paused_until)
                FROM harvest_host_state s JOIN harvest_host_lane l ON l.host_key=s.host_key
                WHERE s.host_key=@host AND l.lane=@lane AND NOT @ok AND @network)),
              http_status=@http,discovered=@discovered,operations=@operations::jsonb
              WHERE work_item_id=@id AND attempt=@attempt;
            UPDATE harvest_runs SET pages=pages+1,captured=captured+@captured,new_posts=new_posts+@created,unchanged=unchanged+@unchanged,
              discovered=discovered+@discovered,failed=failed+@failed,requests=requests+@requests,bytes=bytes+@bytes,
              cost_usd_est=cost_usd_est+@cost,enumeration_complete=(enumeration_complete OR @complete) AND completion_reason IS NULL,
              fetch_outcome=CASE WHEN @ok THEN fetch_outcome ELSE @outcome END,message=@message WHERE id=@run
            """, ct, ("id", claim.Item.WorkItemId), ("run", claim.Item.RunId), ("now", now),
            ("state", ok ? "done" : refused ? "blocked" : "failed"), ("outcome", SnakeCaseEnum<FetchOutcome>.ToText(result.Outcome)),
            ("message", result.Message), ("captured", captured), ("created", created), ("unchanged", unchanged), ("discovered", discovered),
            ("retry", result.RetryAfter is { } retry ? now + retry : null), ("http", httpStatus is { } status ? (short)status : null),
            ("host", claim.Item.HostKey), ("lane", claim.Item.Lane), ("network", claim.Frozen.Kind != WorkKind.Process),
            ("attempt", claim.Item.Attempt+1), ("operations", trace.Snapshot()),
            ("failed", ok ? 0 : 1), ("requests", checked((int)Math.Min(result.Usage.Requests, int.MaxValue))),
            ("bytes", result.Usage.Bytes), ("cost", result.Usage.EstimatedUsd), ("complete", result.EnumerationComplete && limit is null), ("ok", ok));
        foreach ((string meter, long units) in new[] { ("requests", result.Usage.Requests), ("bytes", result.Usage.Bytes) })
        {
            if (units <= 0)
                continue;
            await EngineSql.ExecuteAsync(connection, transaction,
                """
                INSERT INTO harvest_usage(id,at,container_id,run_id,work_item_id,pipeline_id,tenant_id,host_key,lane,meter,units,source)
                SELECT @id,@now,c.id,@run,@item,@pipeline,c.tenant_id,@host,@lane,@meter,@units,'estimate'
                FROM harvest_containers c WHERE c.id=@container
                """, ct, ("id", Guid.CreateVersion7(now)), ("now", now), ("run", claim.Item.RunId),
                ("item", claim.Item.WorkItemId), ("pipeline", claim.Item.PipelineId), ("host", claim.Item.HostKey),
                ("lane", claim.Item.Lane), ("meter", meter), ("units", units), ("container", claim.Item.ContainerId));
        }

        await using (NpgsqlCommand pending = EngineSql.Command(connection, transaction,
            "SELECT EXISTS(SELECT 1 FROM harvest_work_item WHERE run_id=@run AND state IN ('queued','running'))", ("run", claim.Item.RunId)))
        {
            if (await pending.ExecuteScalarAsync(ct) is true)
                limit ??= await LimitAsync(connection, transaction, claim, now, ct);
        }
        if (stopForHost || limit is not null)
        {
            await EngineSql.ExecuteAsync(connection, transaction,
                """
                UPDATE harvest_work_item SET state='blocked',finished_at=@now,last_error=@message,lease_owner=NULL,lease_until=NULL
                  WHERE run_id=@run AND state IN ('queued','running');
                UPDATE harvest_runs SET status='partial',finished_at=@now,completion_reason=@reason,
                  enumeration_complete=false WHERE id=@run
                """, ct, ("run", claim.Item.RunId), ("now", now), ("message", result.Message ?? "Run limit reached."),
                ("reason", SnakeCaseEnum<RunReason>.ToText(limit ?? RunReason.SourceOutcome)));
        }
        else
        {
            await EngineSql.ExecuteAsync(connection, transaction,
                """
                UPDATE harvest_runs SET status=CASE WHEN completion_reason IS NOT NULL THEN 'partial'
                    WHEN failed=0 THEN 'completed' WHEN captured+unchanged>0 THEN 'partial' ELSE 'failed' END,
                  completion_reason=coalesce(completion_reason,CASE WHEN failed=0 THEN 'exhausted' ELSE 'detail_failures' END),finished_at=@now
                WHERE id=@run AND NOT EXISTS(SELECT 1 FROM harvest_work_item WHERE run_id=@run AND state IN ('queued','running'))
                """, ct, ("run", claim.Item.RunId), ("now", now));
        }
        await transaction.CommitAsync(ct);
    }

    private async Task<RunReason?> LimitAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        WorkClaim claim,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using NpgsqlCommand command = EngineSql.Command(connection, transaction,
            "SELECT captured+unchanged,requests,bytes,coalesce(started_at,queued_at) FROM harvest_runs WHERE id=@id",
            ("id", claim.Item.RunId));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        PipelineLimits limits = claim.Frozen.Pipeline.Limits;
        if (claim.Frozen.Kind is not (WorkKind.Discover or WorkKind.Process)
            && reader.GetInt32(0) >= (limits.MaxItems ?? options.Value.MaxItems))
            return RunReason.ItemLimit;
        if (reader.GetInt32(1) >= (limits.MaxRequests ?? options.Value.MaxRequests))
            return RunReason.RequestLimit;
        if (reader.GetInt64(2) >= (limits.MaxBytes ?? options.Value.MaxBytes))
            return RunReason.ByteLimit;
        if (now - reader.GetFieldValue<DateTimeOffset>(3) >= (limits.MaxDuration ?? options.Value.MaxDuration))
            return RunReason.TimeLimit;
        return null;
    }

    private async Task BackoffAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        WorkClaim claim,
        StepResult result,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await EngineSql.ExecuteAsync(connection, transaction,
            """
            UPDATE harvest_host_lane SET consecutive_failures=consecutive_failures+1,last_challenge_kind=@challenge,
              paused_until=greatest(coalesce(paused_until,@now),@now+greatest(@retry,
                @backoff*power(2::double precision,least(consecutive_failures,6)))),updated_at=@now
            WHERE host_key=@host AND lane=@lane
            """, ct, ("host", claim.Item.HostKey), ("lane", claim.Item.Lane), ("now", now),
            ("retry", result.RetryAfter ?? TimeSpan.Zero), ("backoff", options.Value.RefusalBackoff),
            ("challenge", result.Challenge?.ToString()));
    }

    private static bool IsRefusal(FetchOutcome outcome)
    {
        return outcome is FetchOutcome.Challenge or FetchOutcome.WaitingRoom or FetchOutcome.Blocked
            or FetchOutcome.RateLimited or FetchOutcome.RobotsDisallowed or FetchOutcome.Restricted or FetchOutcome.LoginRequired;
    }

    private sealed record WorkClaim(StepItem Item, FrozenRun Frozen, ResolvedPlan Plan, string Owner);
}
