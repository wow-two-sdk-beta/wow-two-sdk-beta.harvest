using System.Text.Json;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;
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

        var stage = StageOptions(pipeline, kind);
        if (stage?.Disabled is true)
            return Result<Guid>.Fail(AppErrorFactory.Conflict("This stage is disabled."));

        await using (NpgsqlCommand active = EngineSql.Command(connection, transaction,
            "SELECT id FROM harvest_runs WHERE pipeline_id = @pipeline AND kind = @kind AND status IN ('queued','running','paused')",
            ("pipeline", pipeline.Id), ("kind", SnakeCaseEnum<WorkKind>.ToText(kind))))
        {
            if (await active.ExecuteScalarAsync(ct) is Guid existing)
                return Result<Guid>.Ok(existing);
        }

        ISourceAdapter? adapter = adapters.LastOrDefault(candidate => candidate.Serves(source));
        AdapterDescriptor descriptor = adapter?.Describe(source) ?? new AdapterDescriptor { Steps = [], Plans = new Dictionary<WorkKind, IReadOnlyList<string>>() };
        var steps = StageSteps(descriptor, kind);
        if (kind == WorkKind.Process)
            descriptor = descriptor with { Steps = [.. descriptor.Steps, new StepDescriptor { Key = ProcessingStore.Step, Role = StepRole.Extract }] };
        var catalog = kind == WorkKind.Discover ? DiscoverySection(adapter, source, pipeline.Scope) : null;
        bool available = steps.Count > 0 && (kind != WorkKind.Discover || catalog is not null);
        string? initialTarget = null;
        if (available && kind is not (WorkKind.Check or WorkKind.Refresh or WorkKind.Harvest or WorkKind.Process))
        {
            initialTarget = catalog?.Url ?? pipeline.Scope.Url
                ?? $"https://{source.Handle}/{(steps[0] == "enumerate" ? "sitemap.xml" : "")}";
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
        var limits = kind switch
        {
            WorkKind.Discover => pipeline.Limits with { MaxPages = null },
            WorkKind.Harvest => pipeline.Limits with { MaxItems = stage!.BatchSize },
            _ => pipeline.Limits,
        };
        var frozen = new FrozenRun(pipeline with { Limits = limits }, source, adapter?.Key ?? "unavailable", descriptor)
        {
            Kind = kind, Stage = stage,
            CatalogEntry = kind == WorkKind.Discover && initialTarget is not null ? DiscoveryStore.Address(initialTarget) : null,
        };
        Guid runId = Guid.CreateVersion7(now);
        await EngineSql.ExecuteAsync(connection, transaction,
            """
            INSERT INTO harvest_runs(id,container_id,pipeline_id,schedule_id,kind,trigger,scheduled_for,frozen,status,steps,
                queued_at,finished_at,fetch_outcome,completion_reason,message,new_posts)
            VALUES(@id,@container,@pipeline,@schedule,@kind,@trigger,@slot,@frozen::jsonb,@status,@steps::jsonb,@now,
                @finished,@outcome,@reason,@message,0)
            ON CONFLICT (pipeline_id,kind,scheduled_for) DO NOTHING
            """, ct,
            ("id", runId), ("container", pipeline.ContainerId), ("pipeline", pipeline.Id), ("schedule", scheduleId),
            ("kind", SnakeCaseEnum<WorkKind>.ToText(kind)), ("trigger", scheduleId is null ? "manual" : "schedule"),
            ("slot", scheduledFor ?? now), ("frozen", StoreJson.Write(frozen)), ("status", available ? "queued" : "failed"),
            ("steps", StoreJson.Write(steps)), ("now", now), ("finished", available ? null : now),
            ("outcome", available ? null : "capability_unavailable"), ("reason", available ? null : "source_outcome"),
            ("message", available ? null : kind == WorkKind.Discover && catalog is null
                ? "This pipeline has no published discovery catalog." : "No registered adapter offers this work kind."));

        await using (NpgsqlCommand actual = EngineSql.Command(connection, transaction,
            "SELECT id FROM harvest_runs WHERE pipeline_id=@pipeline AND kind=@kind AND scheduled_for=@slot",
            ("pipeline", pipeline.Id), ("kind", SnakeCaseEnum<WorkKind>.ToText(kind)), ("slot", scheduledFor ?? now)))
        {
            runId = (Guid)(await actual.ExecuteScalarAsync(ct))!;
        }
        if (!available)
            return Result<Guid>.Ok(runId);

        if (kind == WorkKind.Discover)
        {
            await DiscoveryStore.SeedCatalogAsync(connection, transaction, frozen, runId, steps[0], frozen.CatalogEntry!, now, ct);
        }
        else if (kind == WorkKind.Harvest)
        {
            await DiscoveryStore.SeedDetailsAsync(connection, transaction, frozen, runId, steps[0], now, ct);
        }
        else if (kind == WorkKind.Process)
        {
            await ProcessingStore.SeedAsync(connection, transaction, frozen, runId, now, ct);
        }
        else if (kind is WorkKind.Check or WorkKind.Refresh)
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

    internal static PipelineStageOptions? StageOptions(PipelineModel pipeline, WorkKind kind) =>
        pipeline.Schedules.FirstOrDefault(row => row.Kind == kind)?.Stage ?? kind switch
        {
            WorkKind.Discover => new PipelineStageOptions { BatchSize = pipeline.Limits.MaxPages ?? 10 },
            WorkKind.Harvest => new PipelineStageOptions { BatchSize = pipeline.Limits.MaxItems ?? 50 },
            WorkKind.Process => new PipelineStageOptions { BatchSize = 50 },
            _ => null,
        };

    internal static IReadOnlyList<string> StageSteps(AdapterDescriptor descriptor, WorkKind kind) => kind switch
    {
        WorkKind.Discover => (descriptor.Plans.GetValueOrDefault(WorkKind.Discover)
            ?? descriptor.Plans.GetValueOrDefault(WorkKind.Capture) ?? [])
            .Where(key => descriptor.Steps.Any(step => step.Key == key && step.Role == StepRole.Enumerate)).ToArray(),
        WorkKind.Harvest => descriptor.Steps.Where(step => step.Role == StepRole.Detail && !step.Optional)
            .Select(step => step.Key).Take(1).ToArray(),
        WorkKind.Process => [ProcessingStore.Step],
        _ => descriptor.Plans.GetValueOrDefault(kind) ?? [],
    };

    internal static SourceSectionDefinition? DiscoverySection(ISourceAdapter? adapter, SourceModel source, PipelineScope scope)
    {
        if (adapter is not ISourceSectionCatalog registry || scope.Parameters.Count != 0)
            return null;
        var sections = registry.Sections(source).Where(section => InScope(section.Url, source, scope)).ToArray();
        if (scope.Url is null)
            return sections.Length == 1 ? sections[0] : null;
        var (key, _) = RegistrationRules.CanonicalScopeKey(new PipelineScope { Url = scope.Url });
        return key is null ? null : sections.FirstOrDefault(section =>
            RegistrationRules.CanonicalScopeKey(new PipelineScope { Url = section.Url }).ScopeKey == key);
    }

    internal static bool InScope(string target, SourceModel source, PipelineScope scope)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.Equals(uri.IdnHost, source.Handle, StringComparison.OrdinalIgnoreCase))
            return false;
        if (Uri.TryCreate(scope.Url, UriKind.Absolute, out Uri? root))
            return uri.Scheme == root.Scheme && uri.Port == root.Port && scope.IncludesUrl(target);
        return !scope.RestrictToPath && uri.IsDefaultPort;
    }

    internal static async Task<EnqueueResult> EnqueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FrozenRun frozen,
        Guid runId,
        StepTarget target,
        DateTimeOffset now,
        CancellationToken ct)
    {
        StepDescriptor? step = frozen.Adapter.Steps.SingleOrDefault(candidate => candidate.Key == target.StepKey);
        if (step is null || (frozen.Kind != WorkKind.Process && !InScope(target.Target, frozen.Source, frozen.Pipeline.Scope)))
            return EnqueueResult.Skipped;
        frozen.Pipeline.Steps.TryGetValue(step.Key, out StepOverride? change);
        if (step.Optional && change?.Enabled != true)
            return EnqueueResult.Skipped;
        string address = frozen.Kind == WorkKind.Process ? target.Target : DiscoveryStore.Address(target.Target);
        if (frozen.Pipeline.Limits.MaxPages is { } pageLimit && PageSteps(frozen) is var pageSteps
            && pageSteps.Contains(step.Key, StringComparer.Ordinal))
        {
            // Admission runs under the run's row lock. Count the whole frontier, including work still queued.
            await using NpgsqlCommand count = EngineSql.Command(connection, transaction,
                """
                SELECT CASE WHEN EXISTS(SELECT 1 FROM harvest_work_item WHERE run_id=@run AND step=@step AND target=@target)
                    THEN -1 ELSE (SELECT count(*)::int FROM harvest_work_item WHERE run_id=@run AND step=ANY(@pages)) END
                """, ("run", runId), ("step", step.Key), ("target", address), ("pages", pageSteps));
            int admitted = (int)(await count.ExecuteScalarAsync(ct))!;
            if (admitted < 0)
                return EnqueueResult.Skipped;
            if (admitted >= pageLimit)
                return EnqueueResult.PageLimit;
        }
        StepCapabilityPolicy? policy = frozen.Source.CapabilityPolicy.Steps.GetValueOrDefault(step.Key);
        string lane = frozen.Kind == WorkKind.Process ? ProcessingStore.Step
            : change?.Lane ?? policy?.Lane ?? frozen.Source.CapabilityPolicy.Defaults?.Lane ?? step.Lane;
        var plan = new ResolvedPlan { StepKey = step.Key, HostKey = frozen.Source.Handle, Lane = lane, Requirements = step.Requires };
        await EngineSql.ExecuteAsync(connection, transaction,
            "INSERT INTO harvest_host_state(host_key,next_allowed_at,updated_at) VALUES(@host,@now,@now) ON CONFLICT DO NOTHING", ct,
            ("host", plan.HostKey), ("now", now));
        await EngineSql.ExecuteAsync(connection, transaction,
            "INSERT INTO harvest_host_lane(host_key,lane) VALUES(@host,@lane) ON CONFLICT DO NOTHING", ct,
            ("host", plan.HostKey), ("lane", lane));
        int added = await EngineSql.ExecuteAsync(connection, transaction,
            """
            INSERT INTO harvest_work_item(id,container_id,run_id,pipeline_id,host_key,step,lane,plan_ref,route,target,payload,
                priority,due_at,not_before,created_at)
            VALUES(@id,@container,@run,@pipeline,@host,@step,@lane,@plan::jsonb,'static',@target,@payload::jsonb,@priority,@now,@now,@now)
            ON CONFLICT(run_id,step,target) DO NOTHING
            """, ct, ("id", Guid.CreateVersion7(now)), ("container", frozen.Pipeline.ContainerId), ("run", runId),
            ("pipeline", frozen.Pipeline.Id), ("host", plan.HostKey), ("step", step.Key), ("lane", lane),
            ("plan", StoreJson.Write(plan)), ("target", address),
            ("payload", target.Payload?.GetRawText()), ("priority", (short)(target.Priority ?? WorkPriority.Fresh)), ("now", now));
        return added == 0 ? EnqueueResult.Skipped : EnqueueResult.Enqueued;
    }

    /// <summary>Names the steps whose pages count against <see cref="PipelineLimits.MaxPages"/>.</summary>
    /// <remarks>A run whose plan reads listings counts its listing pages and the detail pages they admit alike, so the
    /// limit bounds every page it reads. Any other run, such as a refresh or a staged harvest of saved listings, counts
    /// listing pages only.</remarks>
    internal static string[] PageSteps(FrozenRun frozen)
    {
        IReadOnlyList<string> plan = frozen.Adapter.Plans.GetValueOrDefault(frozen.Kind) ?? [];
        bool readsListings = frozen.Adapter.Steps.Any(step => step.Role == StepRole.Enumerate && plan.Contains(step.Key));
        return [.. frozen.Adapter.Steps
            .Where(step => step.Role == StepRole.Enumerate || (readsListings && step.Role == StepRole.Detail))
            .Select(step => step.Key)];
    }

    internal enum EnqueueResult { Skipped, Enqueued, PageLimit }
}
