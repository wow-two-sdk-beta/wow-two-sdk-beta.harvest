using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace HarvestSources.Api.Pipelines;

/// <summary>Answers <c>pipelines</c>: sets a host's reading pace, queues one pipeline run, sets one schedule's interval and
/// reads a pipeline's state from the deployed host, so a platform job does each with no operator token — the verb forms of
/// <c>PUT api/sources/{id}/reading</c>, <c>POST api/runs</c>, <c>PUT api/pipelines/{id}</c> and <c>GET api/pipelines/{id}</c>.</summary>
/// <remarks>
///   - <c>pipelines run --pipeline &lt;id&gt; [--kind capture|check|refresh]</c> queues one run (capture by default) and is
///     refused while the pipeline has a queued or running run of that kind; the last stdout line is
///     <c>{"pipelineId","runId","kind","status"}</c>
///   - <c>pipelines pace --source &lt;id|family/handle&gt; --ms &lt;1000-60000&gt; [--tenant &lt;slug&gt;]</c> sets the least gap
///     between two requests to the source's host, which every source on that host shares; a source is named as
///     <c>sources processing</c> names it, and the last line is <c>{"sourceId","host","minDelayMs"}</c>
///   - <c>pipelines schedule --pipeline &lt;id&gt; --kind capture|check|refresh --every &lt;duration&gt; [--off]</c> makes the
///     schedule of that kind an interval schedule (<c>15m</c>, <c>1h</c>, <c>2h30m</c> or <c>01:00:00</c>, at least five
///     minutes), disabled with <c>--off</c>; every other schedule, the name, the scope and the limits stay as stored, and a
///     pipeline with no schedule of that kind gets one; the last line is
///     <c>{"pipelineId","schedules":[{"kind","mode","every","cron","enabled","nextDueAtUtc"}]}</c>
///   - <c>pipelines show --pipeline &lt;id&gt; [--runs &lt;n&gt;]</c> writes nothing; the last line carries the pipeline's name,
///     state, limits and schedules and its newest <c>n</c> runs (5 by default, 0–20), with counters only: no post text and
///     no URL
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
        Usage: pipelines run --pipeline <id> [--kind <capture|check|refresh>]
               pipelines pace --source <id|family/handle> --ms <1000-60000> [--tenant <slug>]
               pipelines schedule --pipeline <id> --kind <capture|check|refresh> --every <15m|1h|2h30m|01:00:00> [--off]
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
            || scope.ServiceProvider.GetService<IRunBroker>() is not { } runs)
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
                "schedule" => await ScheduleAsync(registry, request, error, ct),
                _ => await ShowAsync(registry, runs, request, ct),
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

    private static async Task<object> ScheduleAsync(IRegistrationBroker registry, Request request, TextWriter error, CancellationToken ct)
    {
        var pipelineId = request.Pipeline!.Value;
        var kind = request.Kind!.Value;
        var every = request.Every!.Value;
        if (every < PipelineConstants.MinInterval)
            throw new Refusal($"--every must be at least {PipelineConstants.MinInterval.TotalMinutes:0} minutes.");

        var pipeline = Value(await registry.GetPipelineAsync(pipelineId, ct));
        var schedules = pipeline.Schedules.Select(Declare).ToList();
        var index = schedules.FindIndex(schedule => schedule.Kind == kind);
        var changed = (index >= 0 ? schedules[index] : new ScheduleDeclaration { Kind = kind, Mode = ScheduleMode.Interval })
            with { Mode = ScheduleMode.Interval, Every = every, MaxEvery = null, Cron = null, Enabled = !request.Off };
        if (index >= 0)
            schedules[index] = changed;
        else
            schedules.Add(changed);

        var saved = Value(await registry.UpdatePipelineAsync(pipelineId,
            new PipelineUpdate { Name = pipeline.Name, Scope = pipeline.Scope, Schedules = schedules }, ct));
        await error.WriteLineAsync($"{Snake(kind)} of pipeline {pipelineId:D} runs every {Duration(every)}{(request.Off ? ", off" : "")}");
        return new { pipelineId, schedules = saved.Schedules.OrderBy(schedule => schedule.Kind).Select(Describe).ToArray() };
    }

    private static async Task<object> ShowAsync(IRegistrationBroker registry, IRunBroker runs, Request request, CancellationToken ct)
    {
        var pipelineId = request.Pipeline!.Value;
        var pipeline = Value(await registry.GetPipelineAsync(pipelineId, ct));
        int count = request.Runs ?? DefaultRuns;
        IReadOnlyList<RunModel> recent = count == 0 ? [] : Value(await runs.ListAsync(pipelineId, count, ct));
        var limits = pipeline.Limits;
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
            runs = recent.OrderByDescending(run => run.QueuedAtUtc).Select(run => new
            {
                runId = run.Id,
                kind = Snake(run.Kind),
                status = Snake(run.Status),
                fetchOutcome = run.FetchOutcome is { } outcome ? Snake(outcome) : null,
                completionReason = run.CompletionReason is { } completion ? Snake(completion) : null,
                requests = run.Requests,
                captured = run.Captured,
                newPosts = run.NewPosts,
                failed = run.Failed,
                startedAtUtc = run.StartedAtUtc,
                finishedAtUtc = run.FinishedAtUtc,
            }).ToArray(),
        };
    }

    private static object Describe(ScheduleModel schedule) => new
    {
        kind = Snake(schedule.Kind), mode = Snake(schedule.Mode), every = schedule.Every is { } every ? Duration(every) : null,
        cron = schedule.Cron, enabled = schedule.Enabled, nextDueAtUtc = schedule.NextDueAtUtc,
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
        if (arguments is not ["run" or "pace" or "schedule" or "show", ..])
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
            "schedule" => seen.IsSubsetOf(["--pipeline", "--kind", "--every", "--off"])
                && request.Pipeline is not null && request.Kind is not null && request.Every is not null,
            _ => seen.IsSubsetOf(["--pipeline", "--runs"]) && request.Pipeline is not null,
        };
    }

    private static bool TryParseKind(string value, out WorkKind kind)
    {
        foreach (var candidate in new[] { WorkKind.Capture, WorkKind.Check, WorkKind.Refresh })
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
    }

    private sealed record Entry(SourceModel Source, string Label);

    private sealed class Refusal(string message) : Exception(message);
}
