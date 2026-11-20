using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;
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
        IServiceProvider services,
        WorkKind kind,
        DateTimeOffset now,
        Guid? scheduleId,
        DateTimeOffset? scheduledFor,
        CancellationToken ct,
        QueueOutcome? outcome = null)
    {
        await using (NpgsqlCommand gate = EngineSql.Command(connection, transaction,
            "SELECT state FROM harvest_pipelines WHERE id = @id FOR UPDATE", ("id", pipeline.Id)))
        {
            if (await gate.ExecuteScalarAsync(ct) is not "active")
                return Result<Guid>.Fail(AppErrorFactory.Conflict("The pipeline is not active."));
        }

        if (!source.TurnedOn)
            return Result<Guid>.Fail(SourceCatalogConstants.TurnedOff());
        if (source.State != SourceState.Active || source.Permission is not (SourcePermission.Open or SourcePermission.Granted))
            return Result<Guid>.Fail(AppErrorFactory.Forbidden("The source is not available for fetching."));

        var stage = StageOptions(pipeline, kind);
        if (stage?.Disabled is true)
            return Result<Guid>.Fail(AppErrorFactory.Conflict("This stage is disabled."));
        if (kind == WorkKind.Process
            && await CaptureProcessors.PreflightAsync(CaptureProcessors.Resolve(services, stage), source, stage!, ct,
                automatic: scheduleId is not null) is { } processingError)
            return Result<Guid>.Fail(processingError);

        await using (NpgsqlCommand active = EngineSql.Command(connection, transaction,
            "SELECT id FROM harvest_runs WHERE pipeline_id = @pipeline AND kind = @kind AND status IN ('queued','running','paused')",
            ("pipeline", pipeline.Id), ("kind", SnakeCaseEnum<WorkKind>.ToText(kind))))
        {
            if (await active.ExecuteScalarAsync(ct) is Guid existing)
                return Result<Guid>.Ok(existing);
        }

        ISourceAdapter? adapter = adapters.LastOrDefault(candidate => candidate.Serves(source));
        AdapterDescriptor descriptor = adapter?.Describe(source) ?? new AdapterDescriptor { Steps = [], Plans = new Dictionary<WorkKind, IReadOnlyList<string>>() };
        if (SourceAddress.Resolve(source, descriptor) is null)
            return Result<Guid>.Fail(AppErrorFactory.Validation("The adapter's source address is invalid."));
        var steps = StageSteps(descriptor, kind);
        if (kind == WorkKind.Process)
            descriptor = descriptor with { Steps = [.. descriptor.Steps, new StepDescriptor { Key = ProcessingStore.Step, Role = StepRole.Extract }] };
        var catalog = kind == WorkKind.Discover ? DiscoverySection(adapter, source, pipeline.Scope) : null;
        bool available = steps.Count > 0 && (kind != WorkKind.Discover || catalog is not null);
        string? initialTarget = null;
        if (available && kind is not (WorkKind.Check or WorkKind.Refresh or WorkKind.Harvest or WorkKind.Process))
        {
            initialTarget = catalog?.Url ?? pipeline.Scope.Url
                ?? descriptor.SourceUrl ?? $"https://{source.Handle}/{(steps[0] == "enumerate" ? "sitemap.xml" : "")}";
            if (!InScope(initialTarget, source, pipeline.Scope, descriptor))
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
        if (adapter is ISourceRunLimitsResolver resolver)
            limits = resolver.ResolveRunLimits(source, limits, kind);
        if (RegistrationRules.ValidateLimits(limits) is { } limitError)
            return Result<Guid>.Fail(limitError);
        var frozen = new FrozenRun(pipeline with { Limits = limits }, source, adapter?.Key ?? "unavailable", descriptor)
        {
            Kind = kind, Stage = stage,
            CatalogEntry = kind == WorkKind.Discover && initialTarget is not null ? DiscoveryStore.Address(initialTarget) : null,
        };
        Guid runId = Guid.CreateVersion7(now);
        int inserted = await EngineSql.ExecuteAsync(connection, transaction,
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
        if (outcome is not null)
            outcome.Created = inserted == 1;

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
                if (!InScope(url, source, pipeline.Scope, descriptor))
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
        var descriptor = adapter.Describe(source);
        var sections = registry.Sections(source).Where(section => InScope(section.Url, source, scope, descriptor)).ToArray();
        if (scope.Url is null)
            return sections.Length == 1 ? sections[0] : null;
        var (key, _) = RegistrationRules.CanonicalScopeKey(new PipelineScope { Url = scope.Url });
        return key is null ? null : sections.FirstOrDefault(section =>
            RegistrationRules.CanonicalScopeKey(new PipelineScope { Url = section.Url }).ScopeKey == key);
    }

    internal static bool InScope(string target, SourceModel source, PipelineScope scope, AdapterDescriptor? descriptor = null)
    {
        if (SourceAddress.Resolve(source, descriptor) is not { } address
            || !Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) || !address.IncludesOrigin(uri))
            return false;
        if (Uri.TryCreate(scope.Url, UriKind.Absolute, out Uri? root))
            return address.IncludesOrigin(root) && uri.Scheme == root.Scheme && uri.Port == root.Port && scope.IncludesUrl(target);
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
        var sourceAddress = SourceAddress.Resolve(frozen.Source, frozen.Adapter);
        if (step is null || sourceAddress is null
            || (frozen.Kind != WorkKind.Process && !InScope(target.Target, frozen.Source, frozen.Pipeline.Scope, frozen.Adapter)))
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
        var plan = new ResolvedPlan { StepKey = step.Key, HostKey = sourceAddress.HostKey, Lane = lane, Requirements = step.Requires };
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

    /// <summary>Holds how long a known detail's last read counts as recent when no schedule of the run states a cadence.</summary>
    internal static readonly TimeSpan DefaultRecentRead = TimeSpan.FromHours(6);

    /// <summary>Orders a page result's follow-up targets for admission, so a run's page budget reaches new items first.</summary>
    /// <remarks>Groups: detail targets the container holds no link for yet; listing and every other non-detail target;
    /// known details not read within <see cref="RecentReadWindow"/>; known details read since. The first, second and
    /// fourth group keep the page's order; the third reads the least recently read first, never read first, and the
    /// page's order breaks a tie, so a long listing's stale items take turns across runs. Admission still stops at the
    /// page and item limits, so only what fits the budget is read. One set-based query reads the links of all detail
    /// targets of the result.</remarks>
    /// <param name="connection">The open connection.</param>
    /// <param name="transaction">The completion transaction.</param>
    /// <param name="frozen">The run's frozen plan.</param>
    /// <param name="targets">The result's follow-up targets, in page order.</param>
    /// <param name="now">The clock's time.</param>
    /// <param name="ct">The token that cancels the call.</param>
    internal static async Task<IReadOnlyList<StepTarget>> OrderForAdmissionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FrozenRun frozen,
        IReadOnlyList<StepTarget> targets,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (targets.Count < 2)
            return targets;
        var detailSteps = frozen.Adapter.Steps.Where(step => step.Role == StepRole.Detail).Select(step => step.Key)
            .ToHashSet(StringComparer.Ordinal);
        string?[] addresses = [.. targets.Select(target => detailSteps.Contains(target.StepKey) ? TryAddress(target.Target) : null)];
        if (!addresses.Any(address => address is not null))
            return targets;

        Dictionary<string, DateTimeOffset?> known = await ReadKnownDetailsAsync(connection, transaction, frozen,
            [.. addresses.OfType<string>().Distinct(StringComparer.Ordinal)], ct);
        DateTimeOffset staleBefore = now - RecentReadWindow(frozen, now);
        int Group(int index) => !detailSteps.Contains(targets[index].StepKey) ? 1
            : addresses[index] is not { } address || !known.TryGetValue(address, out DateTimeOffset? lastRead) ? 0
            : lastRead is { } read && read >= staleBefore ? 3 : 2;
        DateTimeOffset LastRead(int index) => Group(index) == 2 && addresses[index] is { } address
            ? known[address] ?? DateTimeOffset.MinValue : DateTimeOffset.MinValue;
        return [.. Enumerable.Range(0, targets.Count).OrderBy(Group).ThenBy(LastRead).Select(index => targets[index])];
    }

    /// <summary>Gets how long ago a known detail may have been read for the run to count it as recent: the cadence of the
    /// run's own schedule row (the capture row when the run has none), else <see cref="DefaultRecentRead"/>.</summary>
    /// <param name="frozen">The run's frozen plan.</param>
    /// <param name="now">The clock's time.</param>
    internal static TimeSpan RecentReadWindow(FrozenRun frozen, DateTimeOffset now)
    {
        ScheduleModel? schedule = frozen.Pipeline.Schedules.FirstOrDefault(row => row.Kind == frozen.Kind)
            ?? frozen.Pipeline.Schedules.FirstOrDefault(row => row.Kind == WorkKind.Capture);
        if (schedule is null)
            return DefaultRecentRead;
        if (schedule.Mode == ScheduleMode.Cron)
        {
            try
            {
                var live = schedule with { Enabled = true, Window = null };
                if (ScheduleCalculator.Next(live, now) is { } first && ScheduleCalculator.Next(live, first) is { } second && second > first)
                    return second - first;
            }
            catch (Exception error) when (error is ArgumentException or Cronos.CronFormatException or TimeZoneNotFoundException)
            {
                return DefaultRecentRead;
            }
            return DefaultRecentRead;
        }
        return schedule.Every is { } every && every > TimeSpan.Zero ? every : DefaultRecentRead;
    }

    private static string? TryAddress(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out _) ? DiscoveryStore.Address(target) : null;

    /// <summary>Reads, by address, the detail targets the source already holds a link for, with the newest moment such a link
    /// was seen alive; a suspect or gone link counts as known but never recently read.</summary>
    private static async Task<Dictionary<string, DateTimeOffset?>> ReadKnownDetailsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FrozenRun frozen,
        string[] addresses,
        CancellationToken ct)
    {
        // The link's own url_key hashes its normalized URL as ModulePostBroker does; the key finds a link whose stored
        // external key differs from the address in form only.
        var byKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string address in addresses)
            byKey.TryAdd(Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(
                new UriBuilder(address) { Fragment = string.Empty }.Uri.AbsoluteUri))), address);
        var wanted = addresses.ToHashSet(StringComparer.Ordinal);
        var known = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
        await using NpgsqlCommand command = EngineSql.Command(connection, transaction,
            """
            SELECT l.external_key,l.url,l.url_key,l.state,s.last_seen_at
            FROM harvest_post_links l LEFT JOIN harvest_link_schedule s ON s.link_id=l.id
            WHERE l.container_id=@container AND l.source_id=@source
              AND (l.external_key=ANY(@addresses) OR l.url_key=ANY(@keys))
            """, ("container", frozen.Pipeline.ContainerId), ("source", frozen.Source.Id), ("addresses", addresses),
            ("keys", byKey.Keys.Select(Convert.FromHexString).ToArray()));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            DateTimeOffset? seen = reader.GetString(3) is "suspect" or "gone" || reader.IsDBNull(4)
                ? null : reader.GetFieldValue<DateTimeOffset>(4);
            var matched = new HashSet<string>(StringComparer.Ordinal);
            if (wanted.Contains(reader.GetString(0)))
                matched.Add(reader.GetString(0));
            if (wanted.Contains(reader.GetString(1)))
                matched.Add(reader.GetString(1));
            if (byKey.TryGetValue(Convert.ToHexString(reader.GetFieldValue<byte[]>(2)), out string? viaKey))
                matched.Add(viaKey);
            foreach (string address in matched)
                known[address] = known.TryGetValue(address, out DateTimeOffset? earlier) && (seen is null || earlier > seen)
                    ? earlier : seen;
        }
        return known;
    }

    internal enum EnqueueResult { Skipped, Enqueued, PageLimit }

    /// <summary>Reports to a <see cref="QueueAsync"/> caller whether the call inserted a run or reused one.</summary>
    internal sealed class QueueOutcome
    {
        /// <summary>Gets or sets whether the call inserted a new run row; false when it returned a run already active or failed.</summary>
        internal bool Created { get; set; }
    }
}
