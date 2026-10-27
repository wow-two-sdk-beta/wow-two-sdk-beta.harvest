using System.Text.Json;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Freezes adapters and seeds the durable frontier in the queue transaction.</summary>
internal static class RunPlanner
{
    internal static async Task<Result<Guid>> QueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PipelineModel pipeline,
        SourceModel source,
        IReadOnlyList<ISourceAdapter> adapters,
        WorkKind kind,
        DateTimeOffset now,
        Guid? scheduleId,
        DateTimeOffset? scheduledFor,
        CancellationToken ct)
    {
        await using (NpgsqlCommand gate = EngineSql.Command(connection, transaction,
            "SELECT state FROM harvest_pipelines WHERE id = @id FOR UPDATE", ("id", pipeline.Id)))
        {
            if (await gate.ExecuteScalarAsync(ct) is not "active")
                return Result<Guid>.Fail(AppErrorFactory.Conflict("The pipeline is not active."));
        }

        if (source.State != SourceState.Active || source.Permission is not (SourcePermission.Open or SourcePermission.Granted))
            return Result<Guid>.Fail(AppErrorFactory.Forbidden("The source is not available for fetching."));

        await using (NpgsqlCommand active = EngineSql.Command(connection, transaction,
            "SELECT id FROM harvest_runs WHERE pipeline_id = @pipeline AND kind = @kind AND status IN ('queued','running','paused')",
            ("pipeline", pipeline.Id), ("kind", SnakeCaseEnum<WorkKind>.ToText(kind))))
        {
            if (await active.ExecuteScalarAsync(ct) is Guid existing)
                return Result<Guid>.Ok(existing);
        }

        ISourceAdapter? adapter = adapters.LastOrDefault(candidate => candidate.Serves(source));
        AdapterDescriptor descriptor = adapter?.Describe(source) ?? new AdapterDescriptor { Steps = [], Plans = new Dictionary<WorkKind, IReadOnlyList<string>>() };
        var frozen = new FrozenRun(pipeline, source, adapter?.Key ?? "unavailable", descriptor);
        Guid runId = Guid.CreateVersion7(now);
        IReadOnlyList<string> steps = descriptor.Plans.GetValueOrDefault(kind) ?? [];
        bool available = steps.Count > 0;
        string? initialTarget = null;
        if (available && kind is not (WorkKind.Check or WorkKind.Refresh))
        {
            initialTarget = pipeline.Scope.Url ?? $"https://{source.Handle}/{(steps[0] == "enumerate" ? "sitemap.xml" : "")}";
            if (!InScope(initialTarget, source, pipeline.Scope))
                return Result<Guid>.Fail(AppErrorFactory.Validation("The pipeline URL must remain on its registered source host."));
            if (pipeline.Scope.Parameters.Count > 0)
            {
                var url = new UriBuilder(initialTarget);
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(url.Query)
                    .ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);
                foreach ((string key, string value) in pipeline.Scope.Parameters)
                    query[key] = value;
                url.Query = string.Join('&', query.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
                initialTarget = url.Uri.AbsoluteUri;
            }
        }
        await EngineSql.ExecuteAsync(connection, transaction,
            """
            INSERT INTO harvest_runs(id,container_id,pipeline_id,schedule_id,kind,trigger,scheduled_for,frozen,status,steps,
                queued_at,finished_at,fetch_outcome,completion_reason,message)
            VALUES(@id,@container,@pipeline,@schedule,@kind,@trigger,@slot,@frozen::jsonb,@status,@steps::jsonb,@now,
                @finished,@outcome,@reason,@message)
            ON CONFLICT (pipeline_id,kind,scheduled_for) DO NOTHING
            """, ct,
            ("id", runId), ("container", pipeline.ContainerId), ("pipeline", pipeline.Id), ("schedule", scheduleId),
            ("kind", SnakeCaseEnum<WorkKind>.ToText(kind)), ("trigger", scheduleId is null ? "manual" : "schedule"),
            ("slot", scheduledFor ?? now), ("frozen", StoreJson.Write(frozen)), ("status", available ? "queued" : "failed"),
            ("steps", StoreJson.Write(steps)), ("now", now), ("finished", available ? null : now),
            ("outcome", available ? null : "capability_unavailable"), ("reason", available ? null : "source_outcome"),
            ("message", available ? null : "No registered adapter offers this work kind."));

        await using (NpgsqlCommand actual = EngineSql.Command(connection, transaction,
            "SELECT id FROM harvest_runs WHERE pipeline_id=@pipeline AND kind=@kind AND scheduled_for=@slot",
            ("pipeline", pipeline.Id), ("kind", SnakeCaseEnum<WorkKind>.ToText(kind)), ("slot", scheduledFor ?? now)))
        {
            runId = (Guid)(await actual.ExecuteScalarAsync(ct))!;
        }
        if (!available)
            return Result<Guid>.Ok(runId);

        if (kind is WorkKind.Check or WorkKind.Refresh)
        {
            var links = new List<(Guid Id, string Url, string ExternalKey)>();
            await using (NpgsqlCommand known = EngineSql.Command(connection, transaction,
                """
                SELECT l.id,l.url,l.external_key FROM harvest_post_links l LEFT JOIN harvest_link_schedule s ON s.link_id=l.id
                WHERE l.container_id=@container AND l.source_id=@source AND l.retired_at IS NULL
                  AND (@refresh OR s.next_check_at IS NULL OR s.next_check_at<=@now)
                  AND l.check_capability <> 'none'
                ORDER BY s.next_check_at NULLS FIRST,l.id LIMIT @limit
                """, ("container", pipeline.ContainerId), ("source", source.Id), ("refresh", kind == WorkKind.Refresh),
                ("now", now), ("limit", pipeline.Limits.MaxProbes ?? pipeline.Limits.MaxItems ?? 2000)))
            {
                await using NpgsqlDataReader reader = await known.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    links.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
            }
            foreach ((Guid linkId, string url, string externalKey) in links)
            {
                if (!InScope(url, source, pipeline.Scope))
                    continue;
                await EnqueueAsync(connection, transaction, frozen, runId,
                    new StepTarget { StepKey = steps[0], Target = url, Payload = JsonSerializer.SerializeToElement(new { linkId, externalKey }) }, now, ct);
            }
        }
        else
        {
            await EnqueueAsync(connection, transaction, frozen, runId, new StepTarget { StepKey = steps[0], Target = initialTarget! }, now, ct);
        }

        await EngineSql.ExecuteAsync(connection, transaction,
            """
            UPDATE harvest_runs SET status='completed',completion_reason='no_items',finished_at=@now
            WHERE id=@id AND NOT EXISTS(SELECT 1 FROM harvest_work_item WHERE run_id=@id)
            """, ct, ("id", runId), ("now", now));
        return Result<Guid>.Ok(runId);
    }

    internal static bool InScope(string target, SourceModel source, PipelineScope scope)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.Equals(uri.IdnHost, source.Handle, StringComparison.OrdinalIgnoreCase))
            return false;
        if (Uri.TryCreate(scope.Url, UriKind.Absolute, out Uri? root))
            return uri.Scheme == root.Scheme && uri.Port == root.Port;
        return uri.IsDefaultPort;
    }

    internal static async Task<int> EnqueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FrozenRun frozen,
        Guid runId,
        StepTarget target,
        DateTimeOffset now,
        CancellationToken ct)
    {
        StepDescriptor? step = frozen.Adapter.Steps.SingleOrDefault(candidate => candidate.Key == target.StepKey);
        if (step is null || !InScope(target.Target, frozen.Source, frozen.Pipeline.Scope))
            return 0;
        frozen.Pipeline.Steps.TryGetValue(step.Key, out StepOverride? change);
        if (step.Optional && change?.Enabled != true)
            return 0;
        StepCapabilityPolicy? policy = frozen.Source.CapabilityPolicy.Steps.GetValueOrDefault(step.Key);
        string lane = change?.Lane ?? policy?.Lane ?? frozen.Source.CapabilityPolicy.Defaults?.Lane ?? step.Lane;
        var plan = new ResolvedPlan { StepKey = step.Key, HostKey = frozen.Source.Handle, Lane = lane, Requirements = step.Requires };
        await EngineSql.ExecuteAsync(connection, transaction,
            "INSERT INTO harvest_host_state(host_key,next_allowed_at,updated_at) VALUES(@host,@now,@now) ON CONFLICT DO NOTHING", ct,
            ("host", plan.HostKey), ("now", now));
        await EngineSql.ExecuteAsync(connection, transaction,
            "INSERT INTO harvest_host_lane(host_key,lane) VALUES(@host,@lane) ON CONFLICT DO NOTHING", ct,
            ("host", plan.HostKey), ("lane", lane));
        return await EngineSql.ExecuteAsync(connection, transaction,
            """
            INSERT INTO harvest_work_item(id,container_id,run_id,pipeline_id,host_key,step,lane,plan_ref,route,target,payload,
                priority,due_at,not_before,created_at)
            VALUES(@id,@container,@run,@pipeline,@host,@step,@lane,@plan::jsonb,'static',@target,@payload::jsonb,@priority,@now,@now,@now)
            ON CONFLICT(run_id,step,target) DO NOTHING
            """, ct, ("id", Guid.CreateVersion7(now)), ("container", frozen.Pipeline.ContainerId), ("run", runId),
            ("pipeline", frozen.Pipeline.Id), ("host", plan.HostKey), ("step", step.Key), ("lane", lane),
            ("plan", StoreJson.Write(plan)), ("target", new Uri(target.Target).GetComponents(UriComponents.AbsoluteUri & ~UriComponents.Fragment, UriFormat.UriEscaped)),
            ("payload", target.Payload?.GetRawText()), ("priority", (short)(target.Priority ?? WorkPriority.Fresh)), ("now", now));
    }
}
