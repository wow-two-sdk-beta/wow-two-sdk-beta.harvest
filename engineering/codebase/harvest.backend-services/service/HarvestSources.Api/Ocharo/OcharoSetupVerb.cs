using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

namespace HarvestSources.Api.Ocharo;

/// <summary>Answers <c>ocharo setup</c>: the same idempotent setup as <c>POST api/ocharo/sources/setup</c>, run on the
/// deployed host's services, so a platform job provisions Ocharo's tenant and subscriptions with no operator token.</summary>
/// <remarks>
///   - <c>ocharo setup [--group &lt;id&gt;]... [--tenant &lt;slug&gt;] [--schedules] [--run]</c>; each <c>--group</c>
///     selects a publisher from the registry, and none selects every publisher with a collector; <c>--tenant</c> names
///     the owning tenant, <c>ocharo</c> by default, such as a second tenant for an isolation check
///   - <c>--schedules</c> enables the registry cadences, on existing pipelines too, keeping a tuned interval; <c>--run</c> queues one bounded capture run per pipeline
///     for the running worker; <c>--refresh</c> queues one refresh run per pipeline, which re-reads every known post so
///     a changed reader emits new revisions
///   - stderr carries the report; the last stdout line is one JSON object with the tenant, container, subscriptions
///     and queued runs
///   - <c>ocharo status [--group &lt;id&gt;]... [--tenant &lt;slug&gt;]</c> writes nothing: its last stdout line names
///     the tenant and, per channel, each schedule's cadence and next due time and the latest run of its kind, short
///     enough for a platform job's last-line readout
///   - exit codes: <c>0</c> done, <c>1</c> refused by setup or a run, or nothing to read, <c>2</c> usage
/// </remarks>
public static class OcharoSetupVerb
{
    /// <summary>Holds the verb's name, the first process argument.</summary>
    public const string Name = "ocharo";

    private const string Usage = "Usage: ocharo setup [--group <id>]... [--tenant <slug>] [--schedules] [--run] [--refresh]\n"
        + "       ocharo status [--group <id>]... [--tenant <slug>]";

    /// <summary>Runs the verb over the host's services.</summary>
    /// <param name="services">The built host's services.</param>
    /// <param name="arguments">The arguments after the verb's name.</param>
    /// <param name="output">Standard output: the result line only.</param>
    /// <param name="error">Standard error: the report.</param>
    /// <param name="ct">The token that cancels the verb.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, IReadOnlyList<string> arguments, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        if (arguments is ["status", ..])
            return await StatusAsync(services, arguments, output, error, ct);
        if (!TryParse(arguments, out var groups, out var tenant, out var schedules, out var kinds))
        {
            await error.WriteLineAsync(Usage);
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<OcharoSourceProvisioner>() is not { } provisioner)
        {
            await error.WriteLineAsync("ocharo setup runs on the internal source host; HarvestSources:Audience is public.");
            return 1;
        }

        var setup = await provisioner.EnsureAsync(schedules, ct, includeInstagram: false, groups.Count == 0 ? null : groups, tenant);
        if (setup is not Result<OcharoSetup>.Success { Value: var value })
        {
            await error.WriteLineAsync("ocharo setup refused: " + ((Result<OcharoSetup>.Failure)setup).Error.Message);
            return 1;
        }

        var runs = new List<object>();
        if (kinds.Count > 0)
        {
            var broker = scope.ServiceProvider.GetRequiredService<IRunBroker>();
            foreach (var source in value.Sources)
            foreach (var kind in kinds)
            {
                var started = await broker.StartAsync(source.PipelineId, kind, ct);
                if (started is not Result<RunModel>.Success { Value: var queued })
                {
                    await error.WriteLineAsync($"ocharo setup could not queue a {kind} run for {source.GroupId}/{source.ChannelId}: "
                        + ((Result<RunModel>.Failure)started).Error.Message);
                    return 1;
                }
                runs.Add(new { pipelineId = source.PipelineId, kind = kind == WorkKind.Refresh ? "refresh" : "capture", runId = queued.Id });
            }
        }

        foreach (var source in value.Sources)
            await error.WriteLineAsync($"{source.GroupId}/{source.ChannelId}: pipeline {source.PipelineId}, subscription {source.SubscriptionId}");
        await error.WriteLineAsync($"tenant {value.TenantId}, container {value.ContainerId}, {value.Sources.Count} subscription(s), {runs.Count} run(s) queued");
        await output.WriteLineAsync(JsonSerializer.Serialize(new
        {
            tenantId = value.TenantId,
            containerId = value.ContainerId,
            subscriptions = value.Sources.Select(source => new
            {
                groupId = source.GroupId,
                channelId = source.ChannelId,
                pipelineId = source.PipelineId,
                subscriptionId = source.SubscriptionId,
            }),
            runs,
        }));
        return 0;
    }

    private static async Task<int> StatusAsync(IServiceProvider services, IReadOnlyList<string> arguments, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        var groups = new List<string>();
        string tenant = OcharoSourceProvisioner.DefaultTenantSlug;
        for (var index = 1; index < arguments.Count; index++)
        {
            bool valued = index + 1 < arguments.Count && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal);
            switch (arguments[index])
            {
                case "--group" when valued:
                    groups.Add(arguments[++index]);
                    break;
                case "--tenant" when valued:
                    tenant = arguments[++index];
                    break;
                default:
                    await error.WriteLineAsync(Usage);
                    return 2;
            }
        }

        await using var scope = services.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<OcharoSourceProvisioner>() is not { } provisioner)
        {
            await error.WriteLineAsync("ocharo status runs on the internal source host; HarvestSources:Audience is public.");
            return 1;
        }
        var read = await provisioner.ReadAsync(groups.Count == 0 ? null : groups, tenant, ct);
        if (read is not Result<OcharoStatus>.Success { Value: var status })
        {
            await error.WriteLineAsync("ocharo status: " + ((Result<OcharoStatus>.Failure)read).Error.Message);
            return 1;
        }

        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var lines = new List<string>();
        foreach (var channel in status.Channels)
        {
            string name = channel.GroupId + "/" + channel.ChannelId;
            if (channel.Pipeline is not { } pipeline)
            {
                lines.Add(name + " none");
                continue;
            }
            IReadOnlyList<RunModel> recent = await runs.ListAsync(pipeline.Id, 20, ct) is Result<IReadOnlyList<RunModel>>.Success { Value: var list }
                ? list : [];
            var parts = pipeline.Schedules.OrderBy(schedule => schedule.Kind).Select(schedule =>
            {
                string cadence = schedule.Enabled ? Cadence(schedule) + " due " + Time(schedule.NextDueAtUtc) : "off";
                RunModel? last = recent.Where(run => run.Kind == schedule.Kind).MaxBy(run => run.QueuedAtUtc);
                return $"{Snake(schedule.Kind)} {cadence}" + (last is null ? "" : $" last {Snake(last.Status)}"
                    + (last.CompletionReason is { } reason ? "/" + Snake(reason) : "") + " " + Time(last.FinishedAtUtc ?? last.QueuedAtUtc));
            });
            string schedules = string.Join("; ", parts);
            string line = name + " " + (schedules.Length == 0 ? "no schedules" : schedules);
            lines.Add(line);
            await error.WriteLineAsync($"{line} (pipeline {pipeline.Id})");
        }
        await output.WriteLineAsync(JsonSerializer.Serialize(new { tenant, pipelines = lines }));
        return 0;
    }

    private static string Cadence(ScheduleModel schedule) => schedule switch
    {
        { Cron: { Length: > 0 } } => "cron",
        { Every: { } every } when every.TotalDays >= 1 && every.TotalDays % 1 == 0 => $"{every.TotalDays:0}d",
        { Every: { } every } when every.TotalHours >= 1 && every.TotalHours % 1 == 0 => $"{every.TotalHours:0}h",
        { Every: { } every } => $"{every.TotalMinutes:0}m",
        _ => "on",
    };

    private static string Time(DateTimeOffset? at) =>
        at?.UtcDateTime.ToString("MM-dd'T'HH:mm'Z'", System.Globalization.CultureInfo.InvariantCulture) ?? "-";

    private static string Snake<TEnum>(TEnum value) where TEnum : struct, Enum => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    private static bool TryParse(IReadOnlyList<string> arguments, out List<string> groups, out string tenant, out bool schedules,
        out List<WorkKind> kinds)
    {
        groups = [];
        tenant = OcharoSourceProvisioner.DefaultTenantSlug;
        schedules = false;
        kinds = [];
        if (arguments is not ["setup", ..])
            return false;
        for (var index = 1; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--group" when index + 1 < arguments.Count && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal):
                    groups.Add(arguments[++index]);
                    break;
                case "--tenant" when index + 1 < arguments.Count && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal):
                    tenant = arguments[++index];
                    break;
                case "--schedules":
                    schedules = true;
                    break;
                case "--run" when !kinds.Contains(WorkKind.Capture):
                    kinds.Add(WorkKind.Capture);
                    break;
                case "--refresh" when !kinds.Contains(WorkKind.Refresh):
                    kinds.Add(WorkKind.Refresh);
                    break;
                case "--run" or "--refresh":
                    break;
                default:
                    return false;
            }
        }
        return true;
    }
}
