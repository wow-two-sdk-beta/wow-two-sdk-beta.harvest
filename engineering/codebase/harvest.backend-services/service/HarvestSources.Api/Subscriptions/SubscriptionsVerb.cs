using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace HarvestSources.Api.Subscriptions;

/// <summary>Answers <c>subscriptions</c>: lists, changes, previews, diffs, explains and adopts selector subscriptions from the
/// deployed host, so a platform job runs a consumer's shadow period and switch with no operator token.</summary>
/// <remarks>
///   - <c>subscriptions list [--tenant &lt;slug&gt;]</c> and <c>show --subscription &lt;id|name&gt; [--tenant &lt;slug&gt;]</c> read
///     subscriptions; <c>--subscription</c> takes an id or a name, and a name held by two tenants needs <c>--tenant</c>
///   - <c>selector --subscription … [--set '&lt;selector json&gt;'] [--since &lt;date&gt;] [--by &lt;who&gt;]</c> prints the stored selector, or
///     with <c>--set</c> changes it through <see cref="IDeliveryBroker.SetSelectorAsync"/> (<c>--by</c> defaults to
///     <c>operator</c>, <c>--since</c> is the change window's start); a selector equal to the stored one keeps the version
///   - <c>state --subscription … --state active|suspended|archived</c> moves any subscription; <c>archived</c> is final
///   - <c>preview --subscription … [--since &lt;date&gt;] [--samples N]</c> judges the saved selector over its container's live posts
///     (<see cref="ISubscriptionPreviewBroker"/>), default 10 samples; titles carry no contacts and stop at 140 characters
///   - <c>diff --subscription … --against &lt;filter key&gt; [--since &lt;date&gt;] [--limit N] [--counts-only] [--pending]</c> compares the
///     selector with a registered filter (<see cref="ISubscriptionOperationsBroker.DiffAsync"/>), default 50 listed posts;
///     <c>--counts-only</c> lists none and excludes <c>--limit</c> and <c>--pending</c>
///   - <c>explain --subscription … --post &lt;id&gt;</c> judges one post's current revision
///   - <c>adopt --into &lt;id|name&gt; --from &lt;id&gt;[,&lt;id&gt;…] [--dry-run]</c> writes the posts the older subscriptions hold into the selector's log
///   - stderr carries the report; the last stdout line is one JSON object; Wheelhouse keeps a last line whole only up to 16 KiB,
///     so a longer one is reported on stderr and wants a lower <c>--limit</c> or <c>--samples</c>
///   - exit codes: <c>0</c> done, <c>1</c> refused or not found, <c>2</c> usage (a malformed option or <c>--set</c> document included)
/// </remarks>
public static partial class SubscriptionsVerb
{
    /// <summary>Holds the verb's name, the first process argument.</summary>
    public const string Name = "subscriptions";

    private const int ResultLineLimit = 16 * 1024;
    private const int DefaultDiffLimit = 50;
    private const int DefaultSamples = 10;
    private const int TitleLimit = 140;
    private const string DefaultBy = "operator";

    private const string Usage = """
        Usage: subscriptions list [--tenant <slug>]
               subscriptions show --subscription <id|name> [--tenant <slug>]
               subscriptions selector --subscription <id|name> [--tenant <slug>] [--set '<selector json>'] [--since <yyyy-mm-dd>] [--by <who>]
               subscriptions state --subscription <id|name> [--tenant <slug>] --state <active|suspended|archived>
               subscriptions preview --subscription <id|name> [--tenant <slug>] [--since <date>] [--samples <n>]
               subscriptions diff --subscription <id|name> [--tenant <slug>] --against <filter key> [--since <date>] [--limit <n>] [--counts-only] [--pending]
               subscriptions explain --subscription <id|name> [--tenant <slug>] --post <id>
               subscriptions adopt --into <id|name> --from <id>[,<id>...] [--tenant <slug>] [--dry-run]
        """;

    /// <summary>A <c>--set</c> document is a selector exactly as the wire spells it: a property the selector does not have is
    /// refused, never dropped, so a typo cannot pass for a change.</summary>
    private static readonly JsonSerializerOptions StrictWire = new(JsonOptionsConstants.Default)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly IReadOnlyDictionary<string, Form> Forms = new Dictionary<string, Form>(StringComparer.Ordinal)
    {
        ["list"] = new(["tenant"], [], []),
        ["show"] = new(["subscription", "tenant"], [], ["subscription"]),
        ["selector"] = new(["subscription", "tenant", "set", "since", "by"], [], ["subscription"]),
        ["state"] = new(["subscription", "tenant", "state"], [], ["subscription", "state"]),
        ["preview"] = new(["subscription", "tenant", "since", "samples"], [], ["subscription"]),
        ["diff"] = new(["subscription", "tenant", "against", "since", "limit"], ["counts-only", "pending"], ["subscription", "against"]),
        ["explain"] = new(["subscription", "tenant", "post"], [], ["subscription", "post"]),
        ["adopt"] = new(["into", "from", "tenant"], ["dry-run"], ["into", "from"]),
    };

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
        if (!TryParse(arguments, out var request, out var problem))
        {
            if (problem is not null)
                await error.WriteLineAsync("subscriptions: " + problem);
            await error.WriteLineAsync(Usage);
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var session = new Session(scope.ServiceProvider, output, error, ct);
        try
        {
            return await session.RunAsync(request);
        }
        catch (Refusal refusal)
        {
            await error.WriteLineAsync($"subscriptions {request.Command}: {refusal.Message}");
            return 1;
        }
        catch (DbException)
        {
            await error.WriteLineAsync("The store could not be reached. Check the SCFD harvest migrations.");
            return 1;
        }
    }

    private static bool TryParse(IReadOnlyList<string> arguments, out Request request, out string? problem)
    {
        request = new();
        problem = null;
        if (arguments.Count == 0 || !Forms.TryGetValue(arguments[0], out var form))
            return false;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 1; index < arguments.Count; index++)
        {
            string token = arguments[index];
            if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length == 2)
                return false;
            string name = token[2..];
            if (form.Flags.Contains(name))
            {
                if (!flags.Add(name))
                    return false;
                continue;
            }
            if (!form.Values.Contains(name) || index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(arguments[index + 1]) || !values.TryAdd(name, arguments[++index]))
                return false;
        }
        if (form.Required.Any(name => !values.ContainsKey(name)))
            return false;

        request.Command = arguments[0];
        request.Subscription = values.GetValueOrDefault("subscription") ?? values.GetValueOrDefault("into");
        request.Tenant = values.GetValueOrDefault("tenant");
        request.By = values.GetValueOrDefault("by");
        request.Against = values.GetValueOrDefault("against");
        request.CountsOnly = flags.Contains("counts-only");
        request.Pending = flags.Contains("pending");
        request.DryRun = flags.Contains("dry-run");

        if (values.TryGetValue("since", out var rawSince))
        {
            if (!DateTimeOffset.TryParse(rawSince, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var since))
            {
                problem = "--since is not a date.";
                return false;
            }
            request.Since = since;
        }
        if (values.TryGetValue("limit", out var rawLimit))
        {
            if (!int.TryParse(rawLimit, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) || limit < 1)
                return false;
            request.Limit = limit;
        }
        if (values.TryGetValue("samples", out var rawSamples))
        {
            if (!int.TryParse(rawSamples, NumberStyles.None, CultureInfo.InvariantCulture, out var samples))
                return false;
            request.Samples = samples;
        }
        if (values.TryGetValue("state", out var rawState))
        {
            switch (rawState.ToLowerInvariant())
            {
                case "active": request.State = SubscriptionState.Active; break;
                case "suspended": request.State = SubscriptionState.Suspended; break;
                case "archived": request.State = SubscriptionState.Archived; break;
                default: return false;
            }
        }
        if (values.TryGetValue("post", out var rawPost))
        {
            if (!Guid.TryParseExact(rawPost, "D", out var post))
                return false;
            request.Post = post;
        }
        if (values.TryGetValue("from", out var rawFrom))
        {
            var from = new List<Guid>();
            foreach (var part in rawFrom.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Guid.TryParseExact(part, "D", out var id))
                    return false;
                from.Add(id);
            }
            if (from.Count == 0)
                return false;
            request.From = from;
        }
        if (values.TryGetValue("set", out var rawSet))
        {
            try
            {
                request.Set = JsonSerializer.Deserialize<SubscriptionSelector>(rawSet, StrictWire);
            }
            catch (JsonException exception)
            {
                problem = "--set is not a selector: " + exception.Message;
                return false;
            }
            if (request.Set is null)
            {
                problem = "--set is not a selector.";
                return false;
            }
        }

        if (request.Command == "selector" && request.Set is null && (request.Since is not null || request.By is not null))
        {
            problem = "--since and --by belong to --set.";
            return false;
        }
        if (request.CountsOnly && (request.Pending || request.Limit is not null))
        {
            problem = "--counts-only lists no posts, so it excludes --limit and --pending.";
            return false;
        }
        return true;
    }

    private sealed record Form(string[] Values, string[] Flags, string[] Required);

    private sealed class Request
    {
        internal string Command { get; set; } = "";
        internal string? Subscription { get; set; }
        internal string? Tenant { get; set; }
        internal string? By { get; set; }
        internal string? Against { get; set; }
        internal SubscriptionSelector? Set { get; set; }
        internal DateTimeOffset? Since { get; set; }
        internal SubscriptionState? State { get; set; }
        internal int? Limit { get; set; }
        internal int? Samples { get; set; }
        internal Guid? Post { get; set; }
        internal IReadOnlyList<Guid> From { get; set; } = [];
        internal bool CountsOnly { get; set; }
        internal bool Pending { get; set; }
        internal bool DryRun { get; set; }
    }

    private sealed class Refusal(string message) : Exception(message);

    /// <summary>Runs one parsed command in one service scope.</summary>
    private sealed class Session(IServiceProvider services, TextWriter output, TextWriter error, CancellationToken ct)
    {
        private readonly IDeliveryBroker delivery = services.GetRequiredService<IDeliveryBroker>();
        private readonly IRegistrationBroker registry = services.GetRequiredService<IRegistrationBroker>();
        private IReadOnlyList<TenantModel>? tenants;

        internal async Task<int> RunAsync(Request request)
        {
            if (request.Command == "list")
                return await ListAsync(request);

            var tenant = await FindTenantAsync(request.Tenant);
            var subscription = await FindAsync(request.Subscription!, tenant);
            switch (request.Command)
            {
                case "show":
                    return await ShowAsync(subscription);
                case "state":
                    return await StateAsync(subscription, request.State!.Value);
                case "adopt":
                    return await AdoptAsync(subscription, request);
            }

            var selector = RequireSelector(subscription);
            return request.Command switch
            {
                "selector" => await SelectorAsync(subscription, selector, request),
                "preview" => await PreviewAsync(subscription, selector, request),
                "diff" => await DiffAsync(subscription, request),
                _ => await ExplainAsync(subscription, request.Post!.Value),
            };
        }

        private async Task<int> ListAsync(Request request)
        {
            var tenant = await FindTenantAsync(request.Tenant);
            var all = Value(await delivery.ListSubscriptionsAsync(tenant?.Id, ct));
            var slugs = await SlugsAsync();
            foreach (var subscription in all)
                await error.WriteLineAsync(Row(subscription));
            await error.WriteLineAsync($"{all.Count} subscription(s)");
            await WriteResultAsync(new
            {
                subscriptions = all.Select(subscription => new
                {
                    id = subscription.Id,
                    tenant = slugs.GetValueOrDefault(subscription.TenantId),
                    kind = subscription.Kind,
                    name = subscription.Name,
                    filterKey = subscription.FilterKey,
                    selectorVersion = subscription.SelectorVersion,
                    pipelineId = subscription.PipelineId,
                    schema = subscription.Schema,
                    state = subscription.State,
                    createdAtUtc = subscription.CreatedAtUtc,
                }).ToArray(),
            });
            return 0;
        }

        private async Task<int> ShowAsync(SubscriptionModel subscription)
        {
            await error.WriteLineAsync(Row(subscription));
            await WriteResultAsync(new { tenant = (await SlugsAsync()).GetValueOrDefault(subscription.TenantId), subscription });
            return 0;
        }

        private async Task<int> SelectorAsync(SubscriptionModel subscription, SubscriptionSelector stored, Request request)
        {
            string label = Label(subscription);
            if (request.Set is not { } wanted)
            {
                await error.WriteLineAsync($"{label}: selector v{subscription.SelectorVersion}");
                await WriteResultAsync(new { subscriptionId = subscription.Id, name = subscription.Name, selectorVersion = subscription.SelectorVersion, selector = stored });
                return 0;
            }

            var change = Value(await delivery.SetSelectorAsync(subscription.Id,
                new SelectorChange { Selector = wanted, WindowSinceUtc = request.Since, ChangedBy = request.By ?? DefaultBy }, ct));
            int version = change.Subscription.SelectorVersion ?? 0;
            await error.WriteLineAsync(change.Changed
                ? $"{label}: selector changed to v{version}; window since {Time(change.WindowSinceUtc)}: {change.Considered} considered, {change.NewMatches} new, "
                    + $"{change.Unmatched} unmatched, {change.Rematched} rematched, {change.Pending} pending"
                : $"{label}: selector unchanged at v{version}; nothing written");
            await WriteResultAsync(new
            {
                subscriptionId = subscription.Id,
                name = subscription.Name,
                selectorVersion = version,
                changed = change.Changed,
                windowSinceUtc = change.WindowSinceUtc,
                considered = change.Considered,
                newMatches = change.NewMatches,
                unmatched = change.Unmatched,
                rematched = change.Rematched,
                pending = change.Pending,
            });
            return 0;
        }

        private async Task<int> StateAsync(SubscriptionModel subscription, SubscriptionState wanted)
        {
            var moved = Value(await delivery.SetStateAsync(subscription.Id, wanted, ct));
            await error.WriteLineAsync(subscription.State == moved.State
                ? $"{Label(subscription)}: already {Lower(moved.State)}"
                : $"{Label(subscription)}: {Lower(subscription.State)} -> {Lower(moved.State)}");
            await WriteResultAsync(new { subscriptionId = moved.Id, name = moved.Name, kind = moved.Kind, state = moved.State });
            return 0;
        }

        private async Task<int> PreviewAsync(SubscriptionModel subscription, SubscriptionSelector selector, Request request)
        {
            var preview = services.GetRequiredService<ISubscriptionPreviewBroker>();
            var model = Value(await preview.PreviewAsync(new SubscriptionPreviewRequest
            {
                TenantId = subscription.TenantId,
                ContainerId = await ContainerOfAsync(subscription.Id),
                Selector = selector,
                Schema = subscription.Schema,
                SinceUtc = request.Since,
                Samples = request.Samples ?? DefaultSamples,
            }, ct));

            string label = Label(subscription);
            await error.WriteLineAsync($"{label} v{subscription.SelectorVersion} since {Time(model.SinceUtc)}: {model.Considered} considered, {model.Tagged} tagged, "
                + $"{model.Matched} matched, {model.NoMatch} no match, {model.Pending} pending" + (model.Truncated ? " (truncated)" : ""));
            foreach (var clause in model.Clauses)
                await error.WriteLineAsync($"  {clause.Clause} adds {clause.Adds} removes {clause.Removes}");
            foreach (var source in model.Sources)
                await error.WriteLineAsync($"  source {source.Name ?? source.SourceId.ToString("D")}: {source.Considered} considered, {source.Matched} matched, {source.Pending} pending");
            await WriteResultAsync(new
            {
                subscription = label,
                selectorVersion = subscription.SelectorVersion,
                sinceUtc = model.SinceUtc,
                considered = model.Considered,
                tagged = model.Tagged,
                matched = model.Matched,
                noMatch = model.NoMatch,
                pending = model.Pending,
                truncated = model.Truncated,
                sources = model.Sources,
                clauses = model.Clauses,
                samples = model.Samples.Select(Cleaned).ToArray(),
                nearMisses = model.NearMisses.Select(Cleaned).ToArray(),
            });
            return 0;
        }

        private async Task<int> DiffAsync(SubscriptionModel subscription, Request request)
        {
            var operations = services.GetRequiredService<ISubscriptionOperationsBroker>();
            var diff = Value(await operations.DiffAsync(new SelectorDiffRequest
            {
                SubscriptionId = subscription.Id,
                FilterKey = request.Against!,
                SinceUtc = request.Since,
                Limit = request.Limit ?? DefaultDiffLimit,
                Items = !request.CountsOnly,
                IncludePending = request.Pending,
            }, ct));

            await error.WriteLineAsync($"{diff.Subscription} v{diff.SelectorVersion} against {diff.FilterKey} since {Time(diff.SinceUtc)}: {diff.Posts} posts, "
                + $"{diff.OutOfScope} out of scope, {diff.Both} both, {diff.FilterOnly} filter only, {diff.SelectorOnly} selector only, {diff.Neither} neither, "
                + $"{diff.Pending} pending, {diff.FilterUndecided} filter undecided");
            foreach (var item in diff.Items)
                await error.WriteLineAsync($"  {item.PostId:D} filter {item.Filter} selector {Label(item.Selector)} [{string.Join(", ", item.Clauses)}] {item.Source ?? "-"}");
            await WriteResultAsync(diff);
            return 0;
        }

        private async Task<int> ExplainAsync(SubscriptionModel subscription, Guid post)
        {
            var operations = services.GetRequiredService<ISubscriptionOperationsBroker>();
            var model = Value(await operations.ExplainAsync(subscription.Id, post, ct));
            await error.WriteLineAsync($"{Label(subscription)} v{subscription.SelectorVersion}: post {model.PostId:D} revision {model.Revision} is {Label(model.Verdict)} "
                + $"[{string.Join(", ", model.Clauses)}], log holds {model.Held}");
            await WriteResultAsync(new
            {
                subscription = Label(subscription),
                selectorVersion = subscription.SelectorVersion,
                postId = model.PostId,
                revision = model.Revision,
                held = model.Held,
                verdict = model.Verdict,
                clauses = model.Clauses,
                context = model.Context,
                entityType = model.EntityType,
                domains = model.Domains,
                topics = model.Topics,
            });
            return 0;
        }

        private async Task<int> AdoptAsync(SubscriptionModel into, Request request)
        {
            RequireSelector(into);
            var operations = services.GetRequiredService<ISubscriptionOperationsBroker>();
            var adopted = Value(await operations.AdoptAsync(new SelectorAdoptRequest
            {
                IntoSubscriptionId = into.Id,
                FromSubscriptionIds = request.From,
                DryRun = request.DryRun,
            }, ct));

            await error.WriteLineAsync($"{Label(into)} adopts from {request.From.Count} subscription(s){(adopted.DryRun ? " (dry run, nothing written)" : "")}: "
                + $"{adopted.Considered} considered, {adopted.AlreadyHeld} already held, {adopted.Unmatched} unmatched, {adopted.New} new, {adopted.Pending} pending");
            await WriteResultAsync(new
            {
                into = Label(into),
                intoId = into.Id,
                from = request.From,
                considered = adopted.Considered,
                alreadyHeld = adopted.AlreadyHeld,
                unmatched = adopted.Unmatched,
                @new = adopted.New,
                pending = adopted.Pending,
                dryRun = adopted.DryRun,
            });
            return 0;
        }

        private async Task<TenantModel?> FindTenantAsync(string? slug)
        {
            if (slug is null)
                return null;
            return (await TenantsAsync()).SingleOrDefault(tenant => tenant.Slug == slug) ?? throw new Refusal($"No tenant '{slug}'.");
        }

        private async Task<IReadOnlyList<TenantModel>> TenantsAsync() => tenants ??= Value(await registry.ListTenantsAsync(ct));

        private async Task<Dictionary<Guid, string>> SlugsAsync() => (await TenantsAsync()).ToDictionary(tenant => tenant.Id, tenant => tenant.Slug);

        /// <summary>Finds the subscription an id or a name denotes; a name takes the non-archived holder, so a retired subscription
        /// never shadows its successor.</summary>
        private async Task<SubscriptionModel> FindAsync(string token, TenantModel? tenant)
        {
            if (Guid.TryParseExact(token, "D", out var id))
            {
                var found = Value(await delivery.GetSubscriptionAsync(id, ct));
                return tenant is not null && found.TenantId != tenant.Id
                    ? throw new Refusal($"Subscription {id:D} belongs to another tenant than '{tenant.Slug}'.")
                    : found;
            }

            var named = Value(await delivery.ListSubscriptionsAsync(tenant?.Id, ct)).Where(subscription => subscription.Name == token).ToList();
            var live = named.Where(subscription => subscription.State != SubscriptionState.Archived).ToList();
            var matches = live.Count > 0 ? live : named;
            return matches.Count switch
            {
                0 => throw new Refusal($"No subscription is named '{token}'" + (tenant is null ? "." : $" for tenant '{tenant.Slug}'.")),
                1 => matches[0],
                _ => throw new Refusal($"'{token}' names {matches.Count} subscriptions ({string.Join(", ", matches.Select(match => match.Id.ToString("D")))}); "
                    + "name one by its id or add --tenant."),
            };
        }

        private static SubscriptionSelector RequireSelector(SubscriptionModel subscription) => subscription is { Kind: SubscriptionKind.Selector, Selector: { } selector }
            ? selector
            : throw new Refusal($"Subscription {Label(subscription)} is a {Lower(subscription.Kind)} subscription, not a selector subscription.");

        /// <summary>Reads the container a subscription follows, which the subscription model does not carry.</summary>
        private async Task<Guid> ContainerOfAsync(Guid subscriptionId)
        {
            var dataSource = services.GetRequiredKeyedService<NpgsqlDataSource>(HarvestModuleConstants.ServiceKey);
            await using var command = dataSource.CreateCommand("SELECT container_id FROM harvest_subscriptions WHERE id=@id");
            command.Parameters.AddWithValue("id", subscriptionId);
            return await command.ExecuteScalarAsync(ct) is Guid containerId ? containerId : throw new Refusal("Subscription not found.");
        }

        private async Task WriteResultAsync(object result)
        {
            string line = JsonSerializer.Serialize(result, JsonOptionsConstants.Default);
            int bytes = Encoding.UTF8.GetByteCount(line);
            if (bytes > ResultLineLimit)
                await error.WriteLineAsync($"The result line is {bytes} bytes; Wheelhouse keeps a last line whole only up to {ResultLineLimit / 1024} KiB. Lower --limit or --samples.");
            await output.WriteLineAsync(line);
        }

        private static T Value<T>(Result<T> result) where T : notnull => result switch
        {
            Result<T>.Success success => success.Value,
            Result<T>.Failure failure => throw new Refusal(failure.Error.Message),
            _ => throw new InvalidOperationException("Unexpected broker result."),
        };
    }

    private static string Label(SubscriptionModel subscription) => subscription.Name ?? subscription.Id.ToString("D");

    private static string Label(SelectorVerdict verdict) => JsonNamingPolicy.CamelCase.ConvertName(verdict.ToString());

    private static string Lower<TEnum>(TEnum value) where TEnum : struct, Enum => value.ToString().ToLowerInvariant();

    private static string Time(DateTimeOffset? at) => at?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm'Z'", CultureInfo.InvariantCulture) ?? "-";

    private static string Row(SubscriptionModel subscription) =>
        $"{subscription.Id:D} {Lower(subscription.Kind)} {subscription.Name ?? "-"} "
        + $"{(subscription.Kind == SubscriptionKind.Selector ? $"selector v{subscription.SelectorVersion}" : subscription.FilterKey ?? "-")} {Lower(subscription.State)}";

    private static object Cleaned(PreviewPost post) => new
    {
        postId = post.PostId,
        revision = post.Revision,
        title = Clean(post.Title),
        titleEn = Clean(post.TitleEn),
        verdict = post.Verdict,
        clauses = post.Clauses,
        lastChangedAtUtc = post.LastChangedAtUtc,
    };

    /// <summary>Replaces the contacts in a title the way the diff does (emails, messenger links, handles, phone numbers of nine or
    /// more digits) and cuts it to 140 characters without splitting a surrogate pair.</summary>
    private static string? Clean(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;
        const string Removed = "…";
        string text = Email().Replace(title.Trim(), Removed);
        text = Messenger().Replace(text, Removed);
        text = Handle().Replace(text, Removed);
        text = Phone().Replace(text, match => match.Value.Count(char.IsAsciiDigit) >= 9 ? Removed : match.Value);
        return text.Length <= TitleLimit ? text : text[..(char.IsHighSurrogate(text[TitleLimit - 1]) ? TitleLimit - 1 : TitleLimit)];
    }

    [GeneratedRegex(@"[+\d][\d\s\-().]{7,}\d", RegexOptions.CultureInvariant)]
    private static partial Regex Phone();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    [GeneratedRegex(@"(?<![\w.])@[A-Za-z0-9_]{4,}", RegexOptions.CultureInvariant)]
    private static partial Regex Handle();

    [GeneratedRegex(@"\b(?:t\.me|wa\.me|telegram\.me)/\S*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Messenger();
}
