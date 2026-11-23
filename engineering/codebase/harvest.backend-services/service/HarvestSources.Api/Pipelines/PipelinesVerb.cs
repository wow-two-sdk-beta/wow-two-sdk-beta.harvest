using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace HarvestSources.Api.Pipelines;

/// <summary>Answers <c>pipelines</c>: sets a host's reading pace, queues one pipeline run, sets one schedule's interval, sets
/// how many failed runs in a row turn a pipeline's harvesting schedules off and reads a pipeline's state from the deployed
/// host, so a platform job does each with no operator token — the verb forms of
/// <c>PUT api/sources/{id}/reading</c>, <c>POST api/runs</c>, <c>PUT api/pipelines/{id}</c>, <c>PUT api/pipelines/{id}/stages</c>
/// and <c>GET api/pipelines/{id}</c>.</summary>
/// <remarks>
///   - <c>pipelines run --pipeline &lt;id&gt; [--kind capture|check|refresh|discover|harvest]</c> queues one run (capture by
///     default) and is refused while the pipeline has a queued or running run of that kind; the last stdout line is
///     <c>{"pipelineId","runId","kind","status"}</c>; <c>discover</c> and <c>harvest</c> are the pair that walks a whole
///     catalog, where a <c>capture</c> run restarts at page 1; <c>sweep</c> is a usage error
///   - <c>pipelines pace --source &lt;id|family/handle&gt; --ms &lt;1000-60000&gt; [--tenant &lt;slug&gt;]</c> sets the least gap
///     between two requests to the source's host, which every source on that host shares; a source is named as
///     <c>sources processing</c> names it, and the last line is <c>{"sourceId","host","minDelayMs"}</c>
///   - <c>pipelines schedule --pipeline &lt;id&gt; --kind capture|check|refresh|discover|harvest --every &lt;duration&gt; [--off]
///     [--walk full|new|resume] [--batch &lt;1-500&gt;] [--gap-ms &lt;0-60000|host&gt;] [--parallel &lt;1-16&gt;]</c>
///     makes the schedule of that kind an interval schedule (<c>15m</c>, <c>1h</c>, <c>2h30m</c> or <c>01:00:00</c>, at least
///     five minutes, and for <c>harvest</c> at least 15 seconds, <c>15s</c>), disabled with <c>--off</c>; every other schedule,
///     the name, the scope and the limits stay as stored, and a pipeline with no schedule of that kind gets one; the last line is
///     <c>{"pipelineId","schedules":[{"kind","mode","every","cron","enabled","nextDueAtUtc","stage"}]}</c>; turning a row on that
///     was off (or manual, or stage-disabled) clears a pipeline's automatic turn-off and starts its failed-run count again, and the
///     report line says so; the stage flags tune a stage row, and an omitted flag keeps the stored value:
///       <c>--walk</c> (discover only) reads the whole catalog on every run (<c>full</c>), reads from the first page until a
///       page saves nothing new (<c>new</c>) or resumes at the cursor (<c>resume</c>, the default);
///       <c>--batch</c> (harvest only) is the items a run takes, and <c>--parallel</c> (harvest only) how many it fetches at
///       once;
///       <c>--gap-ms</c> (discover and harvest) is the gap between the starts of the stage's own requests to its host, which
///       replaces the host's pace for them, and <c>host</c> clears it;
///       a flag on another kind, a repeated flag or a non-number is a usage error, and a value out of range is refused with
///       nothing saved; each stage row (discover, harvest, process) prints a <c>stage</c> object
///       <c>{"batch","walk","gapMs","parallel","disabled"}</c> with <c>"resume"</c>, <c>"host"</c> and <c>1</c> for the unset
///       values, in this form and in <c>show</c>; the verb runs as the host's operator, which may set the gap and the parallel
///       fetches
///   - <c>pipelines auto-off --pipeline &lt;id&gt; --after &lt;0-1000|default&gt;</c> sets how many capture, check, refresh,
///     discover or harvest runs in a row may fail before the pipeline's schedules of those kinds turn themselves off (a
///     pipeline's own value wins over <c>Harvest:Worker:AutoOffAfter</c>, 10 by default; <c>0</c> never turns it off;
///     <c>default</c> returns to the host's); it writes only that value, never turns a row on or off and never starts the
///     count again; the last line is <c>{"pipelineId","autoOff":{"after","fromDefault","failures","off"}}</c>
///   - a <c>discover</c> or <c>harvest</c> row is a stage row: it is saved through <see cref="PipelineStages.SaveAsync"/>, as
///     <c>PUT api/pipelines/{id}/stages</c> saves it, with the stage's stored settings (or the defaults a run would use);
///     enabling one while the pipeline has an enabled capture schedule is refused and names the
///     <c>pipelines schedule --kind capture --every &lt;d&gt; --off</c> that comes first
///   - <c>pipelines show --pipeline &lt;id&gt; [--runs &lt;n&gt;]</c> writes nothing; the last line carries the pipeline's name,
///     state, limits and schedules (the discover and harvest stage rows among them), its <c>autoOff</c>
///     (<c>{"after","fromDefault","failures","off":null|{"atUtc","runId","failures","reason"}}</c>) and its newest <c>n</c>
///     runs (5 by default, 0–20), with counters only: no post text and no URL; each run row carries <c>bytes</c>, the metered wire
///     bytes (gzip/br as received) of a run since v0.2.67, the decoded bytes of an earlier one; <c>last24h</c> is
///     <c>{"since","runs","requests","bytes","byKind":[{"kind","runs","requests","bytes"}]}</c> over the pipeline's runs that
///     started (or, never started, were queued) since the UTC cutoff <c>since</c>, read in one read-only query
///   - <c>--tenant</c> narrows <c>--source</c> to one tenant's containers; a <c>family/handle</c> that matches sources of
///     several tenants is refused
///   - exit codes: <c>0</c> done, <c>1</c> refused or not found, <c>2</c> usage
/// </remarks>
public static partial class PipelinesVerb
{
    /// <summary>Holds the verb's name, the first process argument.</summary>
    public const string Name = "pipelines";

    private const int DefaultRuns = 5;
    private const int MaxRuns = 20;
    private const int ActiveRunScan = 200;

    private const string Usage = """
        Usage: pipelines run --pipeline <id> [--kind <capture|check|refresh|discover|harvest>]
               pipelines pace --source <id|family/handle> --ms <1000-60000> [--tenant <slug>]
               pipelines schedule --pipeline <id> --kind <capture|check|refresh|discover|harvest> --every <15s|15m|1h|2h30m|01:00:00> [--off]
                   [--walk <full|new|resume>] [--batch <1-500>] [--gap-ms <0-60000|host>] [--parallel <1-16>]
               pipelines auto-off --pipeline <id> --after <0-1000|default>
               pipelines show --pipeline <id> [--runs <0-20>]
        """;

    /// <summary>Runs the verb over the host's services.</summary>
    /// <param name="services">The built host's services.</param>
    /// <param name="arguments">The arguments after the verb's name.</param>
    /// <param name="output">Standard output: the last line is the result as one JSON object.</param>
    /// <param name="error">Standard error: usage, refusals and a one-line report.</param>
    /// <param name="ct">The token that cancels the verb.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, IReadOnlyList<string> arguments, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        if (!TryParse(arguments, out var request))
        {
            await error.WriteLineAsync(Usage);
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<IRegistrationBroker>() is not { } registry
            || scope.ServiceProvider.GetService<IRunBroker>() is not { } runs
            || scope.ServiceProvider.GetService<PipelineStages>() is not { } stages
            || scope.ServiceProvider.GetService<PipelineAutoOff>() is not { } autoOff)
        {
            await error.WriteLineAsync($"pipelines {request.Command} runs on the internal source host; HarvestSources:Audience is public.");
            return 1;
        }

        try
        {
            object result = request.Command switch
            {
                "run" => await QueueAsync(runs, request, error, ct),
                "pace" => await PaceAsync(registry, runs, request, error, ct),
                "schedule" => await ScheduleAsync(registry, stages, request, error, ct),
                "auto-off" => await AutoOffAsync(autoOff, request, error, ct),
                _ => await ShowAsync(registry, runs, autoOff, scope.ServiceProvider, request, ct),
            };
            await output.WriteLineAsync(JsonSerializer.Serialize(result));
            return 0;
        }
        catch (Refusal refusal)
        {
            await error.WriteLineAsync($"pipelines {request.Command}: {refusal.Message}");
            return 1;
        }
    }

    private static async Task<object> QueueAsync(IRunBroker runs, Request request, TextWriter error, CancellationToken ct)
    {
        var pipelineId = request.Pipeline!.Value;
        var kind = request.Kind ?? WorkKind.Capture;
        var recent = Value(await runs.ListAsync(pipelineId, ActiveRunScan, ct));
        if (recent.FirstOrDefault(run => run.Kind == kind && run.Status is RunStatus.Queued or RunStatus.Running) is { } active)
            throw new Refusal($"Pipeline {pipelineId:D} already has a {Snake(kind)} run {active.Id:D} {Snake(active.Status)}.");

        var started = Value(await runs.StartAsync(pipelineId, kind, ct));
        await error.WriteLineAsync($"{Snake(kind)} run {started.Id:D} {Snake(started.Status)} for pipeline {pipelineId:D}");
        return new { pipelineId, runId = started.Id, kind = Snake(started.Kind), status = Snake(started.Status) };
    }

    private static async Task<object> PaceAsync(IRegistrationBroker registry, IRunBroker runs, Request request, TextWriter error,
        CancellationToken ct)
    {
        var source = Resolve(await ReadSourcesAsync(registry, request.Tenant, ct), request.Source!);
        var health = Value(await runs.SetReadingPaceAsync(source.Source.Id, request.Ms!.Value, ct));
        await error.WriteLineAsync($"{source.Label}: host {health.HostKey} reads at least {health.MinDelayMs} ms apart, for every source on that host");
        return new { sourceId = source.Source.Id, host = health.HostKey, minDelayMs = health.MinDelayMs };
    }

    private static async Task<object> ScheduleAsync(IRegistrationBroker registry, PipelineStages stages, Request request, TextWriter error,
        CancellationToken ct)
    {
        var pipelineId = request.Pipeline!.Value;
        var kind = request.Kind!.Value;
        var every = request.Every!.Value;
        if (every < PipelineConstants.MinIntervalFor(kind))
            throw new Refusal(kind == WorkKind.Harvest
                ? $"--every must be at least {PipelineConstants.MinHarvestInterval.TotalSeconds:0} seconds."
                : $"--every must be at least {PipelineConstants.MinInterval.TotalMinutes:0} minutes.");

        var pipeline = Value(await registry.GetPipelineAsync(pipelineId, ct));
        var saved = kind is WorkKind.Discover or WorkKind.Harvest
            ? await SaveStageAsync(registry, stages, pipeline, kind, every, request, ct)
            : await SaveScheduleAsync(registry, pipeline, kind, every, request.Off, ct);
        string cleared = pipeline.AutoOff is not null && saved.AutoOff is null
            ? "; the automatic turn-off is cleared and its failed-run count starts again" : "";
        string tuning = saved.Schedules.FirstOrDefault(schedule => schedule.Kind == kind) is { Stage: { } stage }
            && kind is WorkKind.Discover or WorkKind.Harvest ? ", " + Settings(kind, stage) : "";
        await error.WriteLineAsync($"{Snake(kind)} of pipeline {pipelineId:D} runs every {Duration(every)}{(request.Off ? ", off" : "")}{tuning}{cleared}");
        return new { pipelineId, schedules = saved.Schedules.OrderBy(schedule => schedule.Kind).Select(Describe).ToArray() };
    }

    /// <summary>Sets a capture, check or refresh row through the pipeline update, which carries every other row as stored.</summary>
    private static async Task<PipelineModel> SaveScheduleAsync(IRegistrationBroker registry, PipelineModel pipeline, WorkKind kind,
        TimeSpan every, bool off, CancellationToken ct)
    {
        var schedules = pipeline.Schedules.Select(Declare).ToList();
        var index = schedules.FindIndex(schedule => schedule.Kind == kind);
        var changed = (index >= 0 ? schedules[index] : new ScheduleDeclaration { Kind = kind, Mode = ScheduleMode.Interval })
            with { Mode = ScheduleMode.Interval, Every = every, MaxEvery = null, Cron = null, Enabled = !off };
        if (index >= 0)
            schedules[index] = changed;
        else
            schedules.Add(changed);

        return Value(await registry.UpdatePipelineAsync(pipeline.Id,
            new PipelineUpdate { Name = pipeline.Name, Scope = pipeline.Scope, Schedules = schedules }, ct));
    }

    /// <summary>Sets a discover or harvest stage row as <c>PUT api/pipelines/{id}/stages</c> does, so no other row is touched.</summary>
    private static async Task<PipelineModel> SaveStageAsync(IRegistrationBroker registry, PipelineStages stages, PipelineModel pipeline,
        WorkKind kind, TimeSpan every, Request request, CancellationToken ct)
    {
        bool off = request.Off;
        if (!off && pipeline.Schedules.FirstOrDefault(schedule => schedule.Kind == WorkKind.Capture) is { Enabled: true, Mode: not ScheduleMode.Manual })
            throw new Refusal($"Pipeline {pipeline.Id:D} has an enabled capture schedule, and a section runs either capture or the discover and "
                + $"harvest stages; run `pipelines schedule --pipeline {pipeline.Id:D} --kind capture --every <d> --off` first.");

        var source = Value(await registry.GetSourceAsync(pipeline.SourceId, ct));
        var stored = pipeline.Schedules.FirstOrDefault(schedule => schedule.Kind == kind);
        var current = stored?.Stage ?? DefaultStage(pipeline, kind);
        // An omitted flag keeps the stored value; the stage is saved whole, as PUT api/pipelines/{id}/stages saves it.
        var tuned = current with
        {
            BatchSize = request.Batch ?? current.BatchSize,
            Walk = request.Walk ?? current.Walk,
            GapMs = request.GapHost ? null : request.GapMs ?? current.GapMs,
            Parallel = request.Parallel ?? current.Parallel,
        };
        var stage = (stored is null ? new ScheduleDeclaration { Kind = kind, Mode = ScheduleMode.Interval } : Declare(stored))
            with { Mode = ScheduleMode.Interval, Every = every, MaxEvery = null, Cron = null, Enabled = !off, Stage = tuned };
        Value(await stages.SaveAsync(pipeline, source, stage, seedOnly: false, ct));
        return Value(await registry.GetPipelineAsync(pipeline.Id, ct));
    }

    /// <summary>Gives a stage with no stored row the batch a run of it would use: the pipeline's page limit for discover, its item
    /// limit for harvest, at most the harvest batch's own limit.</summary>
    private static PipelineStageOptions DefaultStage(PipelineModel pipeline, WorkKind kind) => new()
    {
        BatchSize = kind == WorkKind.Harvest
            ? Math.Min(pipeline.Limits.MaxItems ?? 50, PipelineConstants.MaxHarvestBatch) : pipeline.Limits.MaxPages ?? 10,
    };

    /// <summary>Spells a stage's tuning for the report line: the walk and gap of a discover row, the batch, gap and parallel fetches of a harvest row.</summary>
    private static string Settings(WorkKind kind, PipelineStageOptions stage)
    {
        string gap = stage.GapMs is { } ms ? $"{ms} ms" : "host pace";
        return kind == WorkKind.Discover
            ? $"walk {Snake(stage.Walk ?? DiscoveryWalk.Resume)}, gap {gap}"
            : $"batch {stage.BatchSize}, gap {gap}, parallel {stage.Parallel ?? 1}";
    }

    private static async Task<object> AutoOffAsync(PipelineAutoOff autoOff, Request request, TextWriter error, CancellationToken ct)
    {
        var pipelineId = request.Pipeline!.Value;
        var state = Value(await autoOff.SetAfterAsync(pipelineId, request.After, ct));
        string origin = state.FromDefault ? " (host default)" : "";
        await error.WriteLineAsync(state.After == 0
            ? $"pipeline {pipelineId:D} never turns its harvesting schedules off{origin}; {state.Failures} so far"
            : $"pipeline {pipelineId:D} turns its harvesting schedules off after {state.After} failed runs in a row{origin}; {state.Failures} so far");
        return new { pipelineId, autoOff = DescribeAutoOff(state) };
    }

    private static object DescribeAutoOff(PipelineAutoOffState state) => new
    {
        after = state.After, fromDefault = state.FromDefault, failures = state.Failures,
        off = state.Off is { } off ? new { atUtc = off.AtUtc, runId = off.RunId, failures = off.Failures, reason = off.Reason } : null,
    };

    private static async Task<object> ShowAsync(IRegistrationBroker registry, IRunBroker runs, PipelineAutoOff autoOff,
        IServiceProvider services, Request request, CancellationToken ct)
    {
        var pipelineId = request.Pipeline!.Value;
        var pipeline = Value(await registry.GetPipelineAsync(pipelineId, ct));
        var autoOffState = Value(await autoOff.ReadAsync(pipelineId, ct));
        int count = request.Runs ?? DefaultRuns;
        IReadOnlyList<RunModel> recent = count == 0 ? [] : Value(await runs.ListAsync(pipelineId, count, ct));
        var limits = pipeline.Limits;
        var last24h = await ReadLast24hAsync(services, pipelineId, ct);
        return new
        {
            pipelineId,
            name = pipeline.Name,
            state = Snake(pipeline.State),
            pauseReason = pipeline.PauseReason is { } reason ? Snake(reason) : null,
            limits = new
            {
                pageSize = limits.PageSize, maxPages = limits.MaxPages, maxItems = limits.MaxItems, maxRequests = limits.MaxRequests,
                maxBytes = limits.MaxBytes, maxDuration = limits.MaxDuration is { } duration ? Duration(duration) : null, maxProbes = limits.MaxProbes,
            },
            schedules = pipeline.Schedules.OrderBy(schedule => schedule.Kind).Select(Describe).ToArray(),
            autoOff = DescribeAutoOff(autoOffState),
            last24h,
            runs = recent.OrderByDescending(run => run.QueuedAtUtc).Select(run => new
            {
                runId = run.Id,
                kind = Snake(run.Kind),
                status = Snake(run.Status),
                fetchOutcome = run.FetchOutcome is { } outcome ? Snake(outcome) : null,
                completionReason = run.CompletionReason is { } completion ? Snake(completion) : null,
                requests = run.Requests,
                bytes = run.Bytes,
                captured = run.Captured,
                newPosts = run.NewPosts,
                failed = run.Failed,
                startedAtUtc = run.StartedAtUtc,
                finishedAtUtc = run.FinishedAtUtc,
            }).ToArray(),
        };
    }

    private const string Last24hSql = """
        SELECT kind,count(*),COALESCE(sum(requests),0)::bigint,COALESCE(sum(bytes),0)::bigint
        FROM harvest_runs WHERE pipeline_id=@pipeline AND COALESCE(started_at,queued_at)>=@since
        GROUP BY kind ORDER BY kind
        """;

    /// <summary>Reads the pipeline's runs of the last 24 hours (by start, or by queueing when never started) in one read-only query.</summary>
    private static async Task<object> ReadLast24hAsync(IServiceProvider services, Guid pipelineId, CancellationToken ct)
    {
        var since = (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow().UtcDateTime.AddHours(-24);
        var dataSource = services.GetRequiredKeyedService<NpgsqlDataSource>(HarvestModuleConstants.ServiceKey);
        var kinds = new List<(string Kind, long Runs, long Requests, long Bytes)>();
        await using (var connection = await dataSource.OpenConnectionAsync(ct))
        await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
        {
            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
                await readOnly.ExecuteNonQueryAsync(ct);
            await using var command = new NpgsqlCommand(Last24hSql, connection, transaction);
            command.Parameters.AddWithValue("pipeline", pipelineId);
            command.Parameters.Add(new NpgsqlParameter("since", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = since });
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                kinds.Add((reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)));
        }

        return new
        {
            since,
            runs = kinds.Sum(item => item.Runs), requests = kinds.Sum(item => item.Requests), bytes = kinds.Sum(item => item.Bytes),
            byKind = kinds.Select(item => new
            {
                kind = Enum.TryParse<WorkKind>(item.Kind, true, out var kind) ? Snake(kind) : item.Kind,
                runs = item.Runs, requests = item.Requests, bytes = item.Bytes,
            }).ToArray(),
        };
    }

    private static object Describe(ScheduleModel schedule) => new
    {
        kind = Snake(schedule.Kind), mode = Snake(schedule.Mode), every = schedule.Every is { } every ? Duration(every) : null,
        cron = schedule.Cron, enabled = schedule.Enabled, nextDueAtUtc = schedule.NextDueAtUtc,
        stage = schedule.Kind is WorkKind.Discover or WorkKind.Harvest or WorkKind.Process ? DescribeStage(schedule.Stage) : null,
    };

    /// <summary>Reads a stage row's tuning for the report; an unset walk is <c>resume</c>, an unset gap <c>host</c> and unset parallel fetches 1.</summary>
    private static object DescribeStage(PipelineStageOptions? stage) => new
    {
        batch = stage?.BatchSize, walk = Snake(stage?.Walk ?? DiscoveryWalk.Resume),
        gapMs = stage?.GapMs is { } gap ? (object)gap : "host", parallel = stage?.Parallel ?? 1, disabled = stage?.Disabled is true,
    };

    /// <summary>Reads a stored schedule back as the declaration that keeps it as it is.</summary>
    private static ScheduleDeclaration Declare(ScheduleModel schedule) => new()
    {
        Kind = schedule.Kind, Mode = schedule.Mode, Every = schedule.Every, MaxEvery = schedule.MaxEvery, Cron = schedule.Cron,
        TimeZone = schedule.TimeZone, Window = schedule.Window, Enabled = schedule.Enabled, Stage = schedule.Stage,
    };

    /// <summary>Reads a duration as the verb spells it: <c>15m</c>, <c>1h</c>, <c>2h30m</c>, with <c>d</c> and <c>s</c> too,
    /// or a <c>TimeSpan</c> such as <c>01:00:00</c>.</summary>
    /// <param name="value">The argument.</param>
    /// <param name="duration">The duration when the argument is one.</param>
    /// <returns>Whether the argument is a duration.</returns>
    internal static bool TryParseDuration(string? value, out TimeSpan duration)
    {
        duration = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (value.Contains(':'))
            return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out duration);
        if (DurationPattern().Match(value) is not { Success: true } match)
            return false;
        long Part(string group) => match.Groups[group].Success ? long.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture) : 0;
        duration = TimeSpan.FromSeconds(Part("d") * 86400 + Part("h") * 3600 + Part("m") * 60 + Part("s"));
        return duration > TimeSpan.Zero;
    }

    private static string Duration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration.Ticks % TimeSpan.TicksPerSecond != 0)
            return duration.ToString("c", CultureInfo.InvariantCulture);
        long seconds = (long)duration.TotalSeconds;
        var text = new StringBuilder();
        if (seconds / 3600 > 0)
            text.Append(CultureInfo.InvariantCulture, $"{seconds / 3600}h");
        if (seconds % 3600 / 60 > 0)
            text.Append(CultureInfo.InvariantCulture, $"{seconds % 3600 / 60}m");
        if (seconds % 60 > 0)
            text.Append(CultureInfo.InvariantCulture, $"{seconds % 60}s");
        return text.ToString();
    }

    private static string Snake<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    private static string Label(SourceModel source) =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(source.Family.ToString()) + "/" + source.Handle;

    private static async Task<List<Entry>> ReadSourcesAsync(IRegistrationBroker registry, string? tenantSlug, CancellationToken ct)
    {
        Guid? tenantId = null;
        if (tenantSlug is not null)
        {
            var tenant = Value(await registry.ListTenantsAsync(ct)).SingleOrDefault(item => item.Slug == tenantSlug)
                ?? throw new Refusal($"No tenant '{tenantSlug}'.");
            tenantId = tenant.Id;
        }

        var entries = new List<Entry>();
        foreach (var container in Value(await registry.ListContainersAsync(tenantId, ct)))
            foreach (var source in Value(await registry.ListSourcesAsync(container.Id, ct)))
                entries.Add(new(source, Label(source)));
        return entries;
    }

    private static Entry Resolve(List<Entry> all, string token)
    {
        var matches = (Guid.TryParseExact(token, "D", out var id)
            ? all.Where(entry => entry.Source.Id == id)
            : all.Where(entry => string.Equals(entry.Label, token, StringComparison.OrdinalIgnoreCase))).ToList();
        return matches switch
        {
            [] => throw new Refusal($"No source matches '{token}'."),
            [var only] => only,
            _ => throw new Refusal($"'{token}' matches {matches.Count} sources ({string.Join(", ", matches.OrderBy(entry => entry.Source.Id).Select(entry => entry.Source.Id.ToString("D")))}); "
                + "name one by its id or add --tenant."),
        };
    }

    private static bool TryParse(IReadOnlyList<string> arguments, out Request request)
    {
        request = new();
        if (arguments is not ["run" or "pace" or "schedule" or "auto-off" or "show", ..])
            return false;
        request.Command = arguments[0];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 1; index < arguments.Count; index++)
        {
            string name = arguments[index];
            if (name == "--off")
            {
                if (!seen.Add(name))
                    return false;
                request.Off = true;
                continue;
            }
            string? next = index + 1 < arguments.Count && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(arguments[index + 1]) ? arguments[index + 1] : null;
            if (next is null || !seen.Add(name))
                return false;
            index++;
            switch (name)
            {
                case "--pipeline" when Guid.TryParseExact(next, "D", out var pipeline):
                    request.Pipeline = pipeline;
                    break;
                case "--kind" when TryParseKind(next, out var kind):
                    request.Kind = kind;
                    break;
                case "--source":
                    request.Source = next;
                    break;
                case "--tenant":
                    request.Tenant = next;
                    break;
                case "--ms" when int.TryParse(next, NumberStyles.None, CultureInfo.InvariantCulture, out var ms):
                    request.Ms = ms;
                    break;
                case "--every" when TryParseDuration(next, out var every):
                    request.Every = every;
                    break;
                case "--walk" when TryParseWalk(next, out var walk):
                    request.Walk = walk;
                    break;
                case "--batch" when int.TryParse(next, NumberStyles.None, CultureInfo.InvariantCulture, out var batch):
                    request.Batch = batch;
                    break;
                case "--parallel" when int.TryParse(next, NumberStyles.None, CultureInfo.InvariantCulture, out var parallel):
                    request.Parallel = parallel;
                    break;
                case "--gap-ms" when string.Equals(next, "host", StringComparison.OrdinalIgnoreCase):
                    request.GapHost = true;
                    break;
                case "--gap-ms" when int.TryParse(next, NumberStyles.None, CultureInfo.InvariantCulture, out var gapMs):
                    request.GapMs = gapMs;
                    break;
                case "--after" when TryParseAfter(next, out var after):
                    request.After = after;
                    request.AfterGiven = true;
                    break;
                case "--runs" when int.TryParse(next, NumberStyles.None, CultureInfo.InvariantCulture, out var runs) && runs <= MaxRuns:
                    request.Runs = runs;
                    break;
                default:
                    return false;
            }
        }

        return request.Command switch
        {
            "run" => seen.IsSubsetOf(["--pipeline", "--kind"]) && request.Pipeline is not null,
            "pace" => seen.IsSubsetOf(["--source", "--ms", "--tenant"]) && request.Source is not null && request.Ms is not null,
            "schedule" => seen.IsSubsetOf(["--pipeline", "--kind", "--every", "--off", "--walk", "--batch", "--gap-ms", "--parallel"])
                && request.Pipeline is not null && request.Kind is not null && request.Every is not null
                && FlagsFitKind(request.Kind.Value, seen),
            "auto-off" => seen.IsSubsetOf(["--pipeline", "--after"]) && request.Pipeline is not null && request.AfterGiven,
            _ => seen.IsSubsetOf(["--pipeline", "--runs"]) && request.Pipeline is not null,
        };
    }

    /// <summary>Reads whether the stage flags given belong to the kind: <c>--walk</c> to discover, <c>--batch</c> and
    /// <c>--parallel</c> to harvest, <c>--gap-ms</c> to either.</summary>
    private static bool FlagsFitKind(WorkKind kind, HashSet<string> seen) =>
        (!seen.Contains("--walk") || kind == WorkKind.Discover)
        && (!seen.Contains("--batch") || kind == WorkKind.Harvest)
        && (!seen.Contains("--parallel") || kind == WorkKind.Harvest)
        && (!seen.Contains("--gap-ms") || kind is WorkKind.Discover or WorkKind.Harvest);

    /// <summary>Reads <c>--walk</c>: <c>full</c>, <c>new</c> or <c>resume</c>.</summary>
    private static bool TryParseWalk(string value, out DiscoveryWalk walk)
    {
        foreach (var candidate in new[] { DiscoveryWalk.Full, DiscoveryWalk.New, DiscoveryWalk.Resume })
        {
            if (string.Equals(Snake(candidate), value, StringComparison.OrdinalIgnoreCase))
            {
                walk = candidate;
                return true;
            }
        }
        walk = default;
        return false;
    }

    /// <summary>Reads <c>--after</c>: a count from 0 to 1000, or <c>default</c>, which is no value of the pipeline's own.</summary>
    private static bool TryParseAfter(string value, out int? after)
    {
        after = null;
        if (string.Equals(value, "default", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count > PipelineAutoOff.MaxAfter)
            return false;
        after = count;
        return true;
    }

    private static bool TryParseKind(string value, out WorkKind kind)
    {
        foreach (var candidate in new[] { WorkKind.Capture, WorkKind.Check, WorkKind.Refresh, WorkKind.Discover, WorkKind.Harvest })
        {
            if (string.Equals(Snake(candidate), value, StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }
        kind = default;
        return false;
    }

    private static T Value<T>(Result<T> result) where T : notnull => result switch
    {
        Result<T>.Success success => success.Value,
        Result<T>.Failure failure => throw new Refusal(failure.Error.Message),
        _ => throw new InvalidOperationException("Unexpected broker result."),
    };

    [GeneratedRegex(@"^(?:(?<d>\d{1,5})d)?(?:(?<h>\d{1,5})h)?(?:(?<m>\d{1,6})m)?(?:(?<s>\d{1,7})s)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DurationPattern();

    private sealed class Request
    {
        internal string Command { get; set; } = "";
        internal Guid? Pipeline { get; set; }
        internal WorkKind? Kind { get; set; }
        internal string? Source { get; set; }
        internal string? Tenant { get; set; }
        internal int? Ms { get; set; }
        internal TimeSpan? Every { get; set; }
        internal bool Off { get; set; }
        internal int? Runs { get; set; }
        internal int? After { get; set; }
        internal bool AfterGiven { get; set; }
        internal DiscoveryWalk? Walk { get; set; }
        internal int? Batch { get; set; }
        internal int? GapMs { get; set; }
        internal bool GapHost { get; set; }
        internal int? Parallel { get; set; }
    }

    private sealed record Entry(SourceModel Source, string Label);

    private sealed class Refusal(string message) : Exception(message);
}
