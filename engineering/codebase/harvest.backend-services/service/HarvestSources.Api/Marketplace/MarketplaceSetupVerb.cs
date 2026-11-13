using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

namespace HarvestSources.Api.Marketplace;

/// <summary>Answers <c>marketplace setup|status</c>: the same idempotent setup as <c>POST api/marketplace/{profile}/setup</c>,
/// run on the deployed host's services, so a platform job provisions a product's listing intake with no operator token.</summary>
/// <remarks>
///   - <c>marketplace setup --profile &lt;key&gt; [--schedules] [--run] [--issue-key | --rotate-key]</c>
///   - <c>--schedules</c> enables the profile's capture and check cadences, on existing pipelines too, keeping a tuned
///     interval; <c>--run</c> queues one bounded capture run per search for the running worker
///   - <c>--issue-key</c> issues the consumer's <c>events:read</c> key when it holds no active one; <c>--rotate-key</c>
///     revokes its active keys and issues one. The secret goes only into the last stdout line, once; stderr never carries it
///   - stderr carries the report; the last stdout line is one JSON object with the tenant, container, searches (source,
///     pipeline and subscription each), the consumer key and queued runs
///   - <c>marketplace status --profile &lt;key&gt;</c> writes nothing; its last stdout line names each search's cadence,
///     next due time, latest run of its kind and how many events its subscription's first pull page holds
///     (<c>events 100+</c> past one page), so a deployed host proves delivery with no consumer key
///   - exit codes: <c>0</c> done, <c>1</c> refused by setup or a run, or nothing to read, <c>2</c> usage
/// </remarks>
public static class MarketplaceSetupVerb
{
    /// <summary>Holds the verb's name, the first process argument.</summary>
    public const string Name = "marketplace";

    private static readonly string Usage = "Usage: marketplace setup --profile <key> [--schedules] [--run] [--issue-key | --rotate-key]\n"
        + "       marketplace status --profile <key>\n"
        + "Profiles: " + string.Join(", ", MarketplaceProfiles.All.Select(profile => profile.Key));

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
        if (!TryParse(arguments, out var command, out var profile, out var schedules, out var run, out var keys))
        {
            await error.WriteLineAsync(Usage);
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<MarketplaceProvisioner>() is not { } provisioner)
        {
            await error.WriteLineAsync("marketplace setup runs on the internal source host; HarvestSources:Audience is public.");
            return 1;
        }
        if (command == "status")
            return await StatusAsync(scope.ServiceProvider, provisioner, profile, output, error, ct);

        var setup = await provisioner.EnsureAsync(profile, schedules, keys, ct);
        if (setup is not Result<MarketplaceSetup>.Success { Value: var value })
        {
            await error.WriteLineAsync("marketplace setup refused: " + ((Result<MarketplaceSetup>.Failure)setup).Error.Message);
            return 1;
        }

        var runs = new List<object>();
        if (run)
        {
            var broker = scope.ServiceProvider.GetRequiredService<IRunBroker>();
            foreach (var search in value.Searches)
            {
                var started = await broker.StartAsync(search.PipelineId, WorkKind.Capture, ct);
                if (started is not Result<RunModel>.Success { Value: var queued })
                {
                    await error.WriteLineAsync($"marketplace setup could not queue a capture run for {search.Key}: "
                        + ((Result<RunModel>.Failure)started).Error.Message);
                    return 1;
                }
                runs.Add(new { pipelineId = search.PipelineId, kind = "capture", runId = queued.Id });
            }
        }

        foreach (var search in value.Searches)
            await error.WriteLineAsync($"{value.Profile}/{search.Key}: pipeline {search.PipelineId}, subscription {search.SubscriptionId}");
        string key = value.ConsumerKey is { } consumer
            ? $"key {consumer.Prefix} ({consumer.Id})" + (consumer.Secret is null ? "" : ", issued now; its secret is on stdout only")
            : "no consumer key";
        await error.WriteLineAsync($"tenant {value.TenantId}, container {value.ContainerId}, "
            + $"{value.Searches.Count} subscription(s), {key}, {runs.Count} run(s) queued");
        await output.WriteLineAsync(JsonSerializer.Serialize(new
        {
            profile = value.Profile,
            tenantId = value.TenantId,
            containerId = value.ContainerId,
            searches = value.Searches.Select(search => new
            {
                key = search.Key,
                url = search.Url,
                sourceId = search.SourceId,
                pipelineId = search.PipelineId,
                subscriptionId = search.SubscriptionId,
            }),
            consumerKey = value.ConsumerKey is { } issued ? new { id = issued.Id, prefix = issued.Prefix, secret = issued.Secret } : null,
            runs,
        }));
        return 0;
    }

    private static async Task<int> StatusAsync(IServiceProvider services, MarketplaceProvisioner provisioner, MarketplaceProfile profile,
        TextWriter output, TextWriter error, CancellationToken ct)
    {
        var read = await provisioner.ReadAsync(profile, ct);
        if (read is not Result<MarketplaceStatus>.Success { Value: var status })
        {
            await error.WriteLineAsync("marketplace status: " + ((Result<MarketplaceStatus>.Failure)read).Error.Message);
            return 1;
        }

        var runs = services.GetRequiredService<IRunBroker>();
        var delivery = services.GetRequiredService<IDeliveryBroker>();
        var lines = new List<string>();
        foreach (var search in status.Searches)
        {
            if (search.Pipeline is not { } pipeline)
            {
                lines.Add(search.Key + " none");
                continue;
            }
            IReadOnlyList<RunModel> recent = await runs.ListAsync(pipeline.Id, 20, ct) is Result<IReadOnlyList<RunModel>>.Success { Value: var list }
                ? list : [];
            var parts = pipeline.Schedules.OrderBy(schedule => schedule.Kind).Select(schedule =>
            {
                string cadence = schedule.Enabled ? Cadence(schedule) + " due " + Time(schedule.NextDueAtUtc) : "off";
                RunModel? last = recent.Where(item => item.Kind == schedule.Kind).MaxBy(item => item.QueuedAtUtc);
                return $"{Snake(schedule.Kind)} {cadence}" + (last is null ? "" : $" last {Snake(last.Status)}"
                    + (last.CompletionReason is { } reason ? "/" + Snake(reason) : "") + " " + Time(last.FinishedAtUtc ?? last.QueuedAtUtc));
            });
            string delivered = search.SubscriptionId is not { } subscription ? " no subscription"
                : await delivery.PullAsync(subscription, null, 100, ct) is Result<CursorPage<DeliveryEvent>>.Success { Value: var page }
                    ? $"; events {page.Data.Count}{(page.HasMore ? "+" : "")}" : "; events unreadable";
            string line = search.Key + " " + string.Join("; ", parts) + delivered;
            lines.Add(line);
            await error.WriteLineAsync($"{line} (pipeline {pipeline.Id}, subscription {search.SubscriptionId})");
        }
        await output.WriteLineAsync(JsonSerializer.Serialize(new { profile = status.Profile, activeKeys = status.ActiveKeys, pipelines = lines }));
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

    private static bool TryParse(IReadOnlyList<string> arguments, out string command, out MarketplaceProfile profile, out bool schedules,
        out bool run, out MarketplaceKeyAction keys)
    {
        command = arguments.Count > 0 ? arguments[0] : "";
        profile = null!;
        schedules = false;
        run = false;
        keys = MarketplaceKeyAction.None;
        if (command is not ("setup" or "status"))
            return false;
        bool setup = command == "setup";
        MarketplaceProfile? selected = null;
        for (var index = 1; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--profile" when selected is null && index + 1 < arguments.Count && MarketplaceProfiles.Find(arguments[index + 1]) is { } found:
                    selected = found;
                    index++;
                    break;
                case "--schedules" when setup:
                    schedules = true;
                    break;
                case "--run" when setup:
                    run = true;
                    break;
                case "--issue-key" when setup && keys != MarketplaceKeyAction.Rotate:
                    keys = MarketplaceKeyAction.Issue;
                    break;
                case "--rotate-key" when setup && keys != MarketplaceKeyAction.Issue:
                    keys = MarketplaceKeyAction.Rotate;
                    break;
                default:
                    return false;
            }
        }
        profile = selected!;
        return selected is not null;
    }
}
