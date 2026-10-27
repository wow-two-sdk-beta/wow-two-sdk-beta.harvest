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
        using HttpClient http = clients.CreateClient();
        var context = new EngineStepContext(claim.Plan, claim.Frozen.Source, http, challenges, clock, renderers.FirstOrDefault());
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
            else
                result = await broker.ExecuteAsync(claim.Item, context, lifetime.Token);
            if (result.Outcome == FetchOutcome.RenderRequired && context.Browser is null)
                result = result with { Outcome = FetchOutcome.CapabilityUnavailable, Message = "This page needs the unbound browser renderer." };
            StepSignal? refusal = context.Signals.LastOrDefault(signal => IsRefusal(signal.Outcome));
            if (refusal is not null)
                result = result with { Outcome = refusal.Outcome, Challenge = refusal.Challenge, RetryAfter = refusal.RetryAfter, Captures = [], Next = [], Links = [] };

            if (result.Outcome == FetchOutcome.Ok)
            {
                foreach (Capture capture in result.Captures.Take(claim.Frozen.Pipeline.Limits.MaxItems ?? options.Value.MaxItems))
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
            }
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
        await CompleteAsync(claim, result, readings, cancellationToken);
        return true;
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
              AND (hs.retry_after_until IS NULL OR hs.retry_after_until<=@now)
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
            UPDATE harvest_runs SET status='running',started_at=coalesce(started_at,@now),stage=@step WHERE id=@run
            """, ct, ("id", claim.Item.WorkItemId), ("owner", claim.Owner), ("until", now + options.Value.LeaseDuration),
            ("run", claim.Item.RunId), ("now", now), ("step", claim.Item.StepKey));
        await transaction.CommitAsync(ct);
        return claim;
    }

    private async Task CompleteAsync(
        WorkClaim claim,
        StepResult result,
        IReadOnlyList<(Capture Capture, PostEnvelope Envelope)> readings,
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
        int captured = 0;
        int unchanged = 0;
        var savedKeys = new HashSet<string>(StringComparer.Ordinal);
        if (limit is null && result.Outcome == FetchOutcome.Ok)
        {
            await using NpgsqlCommand count = EngineSql.Command(connection, transaction,
                "SELECT captured+unchanged FROM harvest_runs WHERE id=@run", ("run", claim.Item.RunId));
            int used = (int)(await count.ExecuteScalarAsync(ct))!;
            int remaining = Math.Max(0, (claim.Frozen.Pipeline.Limits.MaxItems ?? options.Value.MaxItems) - used);
            foreach ((Capture capture, PostEnvelope envelope) in readings.Take(remaining))
            {
                Result<CaptureSaveResult> saved = await posts.SaveCaptureAsync(claim.Item.ContainerId, claim.Item.SourceId,
                    claim.Item.RunId, capture, envelope, ct);
                if (saved is Result<CaptureSaveResult>.Failure failure)
                    throw new InvalidOperationException(failure.Error.Message);
                if (((Result<CaptureSaveResult>.Success)saved).Value.Changed)
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
                if (result.Outcome == FetchOutcome.Gone && claim.Item.Payload is null)
                    continue;
                Result<PostModel> observed = await posts.ObserveLinkAsync(claim.Item.ContainerId, claim.Item.SourceId,
                    observation, claim.Item.RunId, ct);
                if (observed is Result<PostModel>.Failure failure)
                    throw new InvalidOperationException(failure.Error.Message);
            }

        int discovered = 0;
        if (limit is null && result.Outcome == FetchOutcome.Ok)
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
                int added = await RunPlanner.EnqueueAsync(connection, transaction, claim.Frozen, claim.Item.RunId, target, now, ct);
                existing += added;
                discovered += added;
            }
        }

        bool ok = result.Outcome is FetchOutcome.Ok or FetchOutcome.Gone;
        bool refused = IsRefusal(result.Outcome);
        if (refused)
            await BackoffAsync(connection, transaction, claim, result, now, ct);
        await EngineSql.ExecuteAsync(connection, transaction,
            """
            UPDATE harvest_work_item SET state=@state,outcome=@outcome,last_error=@message,finished_at=@now,
              lease_owner=NULL,lease_until=NULL WHERE id=@id;
            UPDATE harvest_runs SET pages=pages+1,captured=captured+@captured,unchanged=unchanged+@unchanged,
              discovered=discovered+@discovered,failed=failed+@failed,requests=requests+@requests,bytes=bytes+@bytes,
              cost_usd_est=cost_usd_est+@cost,enumeration_complete=(enumeration_complete OR @complete) AND completion_reason IS NULL,
              fetch_outcome=CASE WHEN @ok THEN fetch_outcome ELSE @outcome END,message=@message WHERE id=@run
            """, ct, ("id", claim.Item.WorkItemId), ("run", claim.Item.RunId), ("now", now),
            ("state", ok ? "done" : refused ? "blocked" : "failed"), ("outcome", SnakeCaseEnum<FetchOutcome>.ToText(result.Outcome)),
            ("message", result.Message), ("captured", captured), ("unchanged", unchanged), ("discovered", discovered),
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
        if (refused || limit is not null)
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
            "SELECT pages,captured+unchanged,requests,bytes,coalesce(started_at,queued_at) FROM harvest_runs WHERE id=@id",
            ("id", claim.Item.RunId));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        PipelineLimits limits = claim.Frozen.Pipeline.Limits;
        if (reader.GetInt32(0) >= (limits.MaxPages ?? int.MaxValue))
            return RunReason.PageLimit;
        if (reader.GetInt32(1) >= (limits.MaxItems ?? options.Value.MaxItems))
            return RunReason.ItemLimit;
        if (reader.GetInt32(2) >= (limits.MaxRequests ?? options.Value.MaxRequests))
            return RunReason.RequestLimit;
        if (reader.GetInt64(3) >= (limits.MaxBytes ?? options.Value.MaxBytes))
            return RunReason.ByteLimit;
        if (now - reader.GetFieldValue<DateTimeOffset>(4) >= (limits.MaxDuration ?? options.Value.MaxDuration))
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
