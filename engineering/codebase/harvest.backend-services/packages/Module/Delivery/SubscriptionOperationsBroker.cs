using System.Data;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Operates a selector subscription's cutover over PostgreSQL: a stateless diff against a filter, a one-post
/// explanation, and an adoption of the posts older subscriptions delivered.</summary>
/// <remarks>
///   - every verdict is <see cref="SubscriptionSelectorMatcher.Evaluate"/> over the stored revision's own envelope, the
///     function the delivery writer, the change window and the preview use
///   - the diff and the explanation read in one repeatable-read transaction and write nothing; the adoption writes under
///     the container lock, through the same <see cref="DeliveryEventWriter.AppendOneAsync"/> a selector change uses
/// </remarks>
internal sealed class SubscriptionOperationsBroker(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource,
    TimeProvider time,
    IEnumerable<IDeliveryPostFilter> filters) : ISubscriptionOperationsBroker
{
    private const int DefaultDiffDays = 7;
    private const int MaxDiffItems = 1_000;
    private const int MaxTitleLength = 140;
    private const string ContentTypes = "'post.new','post.changed','post.extracted','post.unmatched'";

    private static readonly IReadOnlyDictionary<string, string> NoContext = new Dictionary<string, string>();
    private static readonly PostEnvelope AdmissionEnvelope = new() { Extraction = new ExtractionStamp { NormVersion = "operations", RulesVersion = "operations" } };

    private readonly DeliveryPostFilters postFilters = new(filters);

    /// <inheritdoc />
    public async Task<Result<SelectorDiffModel>> DiffAsync(SelectorDiffRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) return Invalid<SelectorDiffModel>("A diff needs its request.");
        if (string.IsNullOrWhiteSpace(request.FilterKey) || request.FilterKey.Length > 200 || !postFilters.Contains(request.FilterKey))
            return Invalid<SelectorDiffModel>("The requested post filter is not registered or its key is invalid.");
        if (request.Limit is < 1 or > MaxDiffItems) return Invalid<SelectorDiffModel>($"The limit must be between 1 and {MaxDiffItems}.");
        var now = time.GetUtcNow();
        var since = request.SinceUtc?.ToUniversalTime() ?? now.AddDays(-DefaultDiffDays);
        if (since > now) return Invalid<SelectorDiffModel>("The diff window cannot start in the future.");
        if (since < now.AddDays(-SelectorRules.MaxWindowDays))
            return Invalid<SelectorDiffModel>($"The diff window reaches at most {SelectorRules.MaxWindowDays} days back.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var subscription = await ReadSubscriptionAsync(connection, transaction, request.SubscriptionId, cancellationToken);
        if (subscription is null) return Missing<SelectorDiffModel>("Subscription not found.");
        if (subscription.Selector is not { } selector || subscription.Version is not { } version)
            return Invalid<SelectorDiffModel>("Only a selector subscription can be compared with a filter.");

        var windowPosts = (long)(await PostSql.ScalarAsync(connection, transaction, """
            SELECT count(*) FROM harvest_posts WHERE container_id=@container AND merged_into IS NULL AND purged_at IS NULL AND last_changed_at>=@since
            """, cancellationToken, ("container", subscription.ContainerId), ("since", since)))!;
        if (windowPosts > SelectorRules.MaxWindowPosts)
            return Invalid<SelectorDiffModel>($"The window holds more than {SelectorRules.MaxWindowPosts} posts; narrow it.");

        var scope = await SelectorStore.ReadScopeAsync(connection, transaction, subscription.ContainerId, [selector], cancellationToken);
        var sources = await ReadSourceNamesAsync(connection, transaction, subscription.ContainerId, cancellationToken);
        var links = await ReadWindowLinksAsync(connection, transaction, subscription.ContainerId, since, scope.Groups, cancellationToken);
        var sourceOnly = new SubscriptionSelector { Sources = selector.Sources };
        bool anySourceClause = selector.Sources.Include.Count > 0 || selector.Sources.Exclude.Count > 0;

        int posts = 0, outOfScope = 0, both = 0, filterOnly = 0, selectorOnly = 0, neither = 0, pending = 0, undecided = 0;
        var filterOnlyItems = new List<SelectorDiffItem>();
        var selectorOnlyItems = new List<SelectorDiffItem>();
        var pendingItems = new List<SelectorDiffItem>();
        await using (var command = PostSql.Command(connection, transaction, """
                         SELECT id,base::text,field_prov::text FROM harvest_posts
                         WHERE container_id=@container AND merged_into IS NULL AND purged_at IS NULL AND last_changed_at>=@since ORDER BY id
                         """, ("container", subscription.ContainerId), ("since", since)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                posts++;
                var postId = reader.GetGuid(0);
                var postLinks = links.TryGetValue(postId, out var found) ? found : [];
                var admitted = anySourceClause ? Admitted(postLinks, sourceOnly, scope.Sections) : postLinks;
                if (admitted.Count == 0)
                {
                    outOfScope++;
                    continue;
                }
                var envelope = StoreJson.Read(reader.GetString(1), new PostEnvelope { Extraction = new ExtractionStamp { NormVersion = "unknown", RulesVersion = "unknown" } });
                var provenance = StoreJson.Read(reader.GetString(2), new Dictionary<string, Guid>());
                Guid? primary = provenance.TryGetValue("title", out var titleLink) ? titleLink : null;
                var evaluation = SubscriptionSelectorMatcher.Evaluate(selector,
                    new SelectorPostFacts { Envelope = envelope, Links = postLinks, PrimaryLinkId = primary }, scope.Sections, subscription.Schema, subscription.Context);
                bool? filter = postFilters.Evaluate(request.FilterKey, envelope);
                bool selectorPasses = evaluation.Verdict == SelectorVerdict.Match;

                if (evaluation.Verdict == SelectorVerdict.Pending)
                {
                    pending++;
                    if (request.Items && request.IncludePending) pendingItems.Add(Item(postId, envelope, admitted, primary, sources, filter, evaluation));
                }
                else if (filter is null) undecided++;
                else if (filter == true && selectorPasses) both++;
                else if (filter == true)
                {
                    filterOnly++;
                    if (request.Items) filterOnlyItems.Add(Item(postId, envelope, admitted, primary, sources, filter, evaluation));
                }
                else if (selectorPasses)
                {
                    selectorOnly++;
                    if (request.Items) selectorOnlyItems.Add(Item(postId, envelope, admitted, primary, sources, filter, evaluation));
                }
                else neither++;
            }
        await transaction.CommitAsync(cancellationToken);

        return Result<SelectorDiffModel>.Ok(new SelectorDiffModel
        {
            Subscription = subscription.Label, SelectorVersion = version, FilterKey = request.FilterKey, SinceUtc = since,
            Posts = posts, OutOfScope = outOfScope, Both = both, FilterOnly = filterOnly, SelectorOnly = selectorOnly, Neither = neither,
            Pending = pending, FilterUndecided = undecided,
            Items = [.. InPostOrder(filterOnlyItems).Concat(InPostOrder(selectorOnlyItems)).Concat(InPostOrder(pendingItems)).Take(request.Items ? request.Limit : 0)],
        });
    }

    /// <inheritdoc />
    public async Task<Result<SelectorExplainModel>> ExplainAsync(Guid subscriptionId, Guid postId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var subscription = await ReadSubscriptionAsync(connection, transaction, subscriptionId, cancellationToken);
        if (subscription is null) return Missing<SelectorExplainModel>("Subscription not found.");
        if (subscription.Selector is not { } selector)
            return Invalid<SelectorExplainModel>("Only a selector subscription has a verdict to explain.");
        if (await ModulePostBroker.ReadPostAsync(connection, transaction, subscription.ContainerId, postId, cancellationToken) is not { } post)
            return Missing<SelectorExplainModel>("Post not found in the subscription's container.");

        var scope = await SelectorStore.ReadScopeAsync(connection, transaction, subscription.ContainerId, [selector], cancellationToken);
        var evaluation = SubscriptionSelectorMatcher.Evaluate(selector, SelectorStore.Facts(post, scope.Groups), scope.Sections, subscription.Schema, subscription.Context);
        var newest = await PostSql.ScalarAsync(connection, transaction, $"""
            SELECT type FROM harvest_subscription_event WHERE subscription_id=@subscription AND post_id=@post AND type IN ({ContentTypes})
            ORDER BY id DESC LIMIT 1
            """, cancellationToken, ("subscription", subscriptionId), ("post", postId)) as string;
        await transaction.CommitAsync(cancellationToken);

        var tags = Describe(post.Base);
        return Result<SelectorExplainModel>.Ok(new SelectorExplainModel
        {
            PostId = post.Id, Revision = post.Revision,
            Held = newest is null ? "none" : newest == DeliveryEventTypes.PostUnmatched ? "unmatched" : "held",
            Verdict = evaluation.Verdict, Clauses = evaluation.Clauses, Context = evaluation.Context,
            EntityType = tags.EntityType, Domains = tags.Domains, Topics = tags.Topics,
        });
    }

    /// <inheritdoc />
    public async Task<Result<SelectorAdoptModel>> AdoptAsync(SelectorAdoptRequest request, CancellationToken cancellationToken = default)
    {
        if (request?.FromSubscriptionIds is null) return Invalid<SelectorAdoptModel>("An adoption needs the subscriptions to adopt from.");
        var from = request.FromSubscriptionIds.Distinct().ToArray();
        if (from.Length == 0) return Invalid<SelectorAdoptModel>("An adoption needs at least one subscription to adopt from.");
        if (from.Contains(request.IntoSubscriptionId)) return Invalid<SelectorAdoptModel>("A subscription cannot adopt its own posts.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await PostSql.ScalarAsync(connection, transaction, "SELECT container_id FROM harvest_subscriptions WHERE id=@id", cancellationToken,
                ("id", request.IntoSubscriptionId)) is not Guid containerId)
            return Missing<SelectorAdoptModel>("Subscription not found.");
        await PostSql.LockAsync(connection, transaction, containerId, cancellationToken);
        var into = await ReadSubscriptionAsync(connection, transaction, request.IntoSubscriptionId, cancellationToken);
        if (into is null) return Missing<SelectorAdoptModel>("Subscription not found.");
        if (into.Selector is not { } selector || into.Version is not { } version)
            return Invalid<SelectorAdoptModel>("Only a selector subscription adopts posts.");
        if (into.State != "active") return Conflict<SelectorAdoptModel>("Only an active selector subscription adopts posts.");
        foreach (var id in from)
        {
            var source = await ReadSubscriptionAsync(connection, transaction, id, cancellationToken);
            if (source is null) return Missing<SelectorAdoptModel>($"Subscription {id} not found.");
            if (source.TenantId != into.TenantId || source.ContainerId != into.ContainerId)
                return Invalid<SelectorAdoptModel>("Every subscription of an adoption belongs to one tenant and one container.");
        }

        var candidates = new List<(Guid PostId, bool Held)>();
        await using (var command = PostSql.Command(connection, transaction, $"""
                         SELECT p.id,EXISTS(SELECT 1 FROM harvest_subscription_event h WHERE h.subscription_id=@into AND h.post_id=p.id)
                         FROM harvest_posts p
                         WHERE p.container_id=@container AND p.merged_into IS NULL AND p.purged_at IS NULL AND p.state='live'
                           AND EXISTS(SELECT 1 FROM unnest(CAST(@from AS uuid[])) AS f(id)
                               CROSS JOIN LATERAL (SELECT e.type FROM harvest_subscription_event e
                                   WHERE e.subscription_id=f.id AND e.post_id=p.id AND e.type IN ({ContentTypes}) ORDER BY e.id DESC LIMIT 1) newest
                               WHERE newest.type<>'post.unmatched')
                         ORDER BY p.first_seen_at,p.id
                         """, ("into", into.Id), ("container", containerId), ("from", from)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                candidates.Add((reader.GetGuid(0), reader.GetBoolean(1)));

        var scope = await SelectorStore.ReadScopeAsync(connection, transaction, containerId, [selector], cancellationToken);
        var now = Now();
        string suffix = $":s{version}";
        int considered = 0, alreadyHeld = 0, unmatched = 0, added = 0, pending = 0;
        foreach (var (postId, held) in candidates)
        {
            considered++;
            if (held)
            {
                alreadyHeld++;
                continue;
            }
            if (await ModulePostBroker.ReadPostAsync(connection, transaction, containerId, postId, cancellationToken) is not { } post) continue;
            var evaluation = SubscriptionSelectorMatcher.Evaluate(selector, SelectorStore.Facts(post, scope.Groups), scope.Sections, into.Schema, into.Context);
            if (evaluation.Verdict == SelectorVerdict.Pending)
            {
                pending++;
                continue;
            }
            var (type, reason) = evaluation.Verdict == SelectorVerdict.NoMatch ? (DeliveryEventTypes.PostUnmatched, "resync") : (DeliveryEventTypes.PostNew, "backfill");
            if (await DeliveryEventWriter.AppendOneAsync(connection, transaction, into.Id, post, type, null, null, reason, now, cancellationToken,
                    SelectorStore.Matched(version, evaluation), suffix) == 0) continue;
            if (type == DeliveryEventTypes.PostNew) added++;
            else unmatched++;
        }

        if (request.DryRun) await transaction.RollbackAsync(cancellationToken);
        else await transaction.CommitAsync(cancellationToken);
        return Result<SelectorAdoptModel>.Ok(new SelectorAdoptModel
        {
            Considered = considered, AlreadyHeld = alreadyHeld, Unmatched = unmatched, New = added, Pending = pending, DryRun = request.DryRun,
        });
    }

    /// <summary>Lists the links a selector's sources admit, one link at a time, over the sources alone: a post none of whose
    /// links is admitted is out of scope however its tags read.</summary>
    private static IReadOnlyList<SelectorLink> Admitted(IReadOnlyList<SelectorLink> links, SubscriptionSelector sourceOnly,
        IReadOnlyDictionary<Guid, SelectorSection> sections) =>
        [.. links.Where(link => SubscriptionSelectorMatcher.Evaluate(sourceOnly, new SelectorPostFacts { Envelope = AdmissionEnvelope, Links = [link] },
            sections, null, NoContext).Verdict == SelectorVerdict.Match)];

    private static SelectorDiffItem Item(Guid postId, PostEnvelope envelope, IReadOnlyList<SelectorLink> admitted, Guid? primary,
        IReadOnlyDictionary<Guid, string> sources, bool? filter, SelectorEvaluation evaluation)
    {
        var contextLink = admitted.FirstOrDefault(link => link.LinkId == primary) ?? admitted[0];
        var tags = Describe(envelope);
        return new SelectorDiffItem
        {
            PostId = postId, Source = sources.GetValueOrDefault(contextLink.SourceId), Title = Clean(envelope.Title), TitleEn = Clean(TitleEn(envelope)),
            Filter = filter is null ? "undecided" : filter == true ? "yes" : "no", Selector = evaluation.Verdict, Clauses = evaluation.Clauses,
            EntityType = tags.EntityType, Domains = tags.Domains, Topics = tags.Topics, Timeframe = tags.Timeframe,
        };
    }

    /// <summary>Orders items by post id as PostgreSQL orders the uuid, so a listing reads oldest post first.</summary>
    private static IEnumerable<SelectorDiffItem> InPostOrder(IEnumerable<SelectorDiffItem> items) =>
        items.OrderBy(item => item.PostId.ToString("N"), StringComparer.Ordinal);

    /// <summary>Reads what the envelope's Layer 1 tags say: the entity type, the most specific domain nodes, the topics and the timeframe.</summary>
    private static (string? EntityType, IReadOnlyList<string> Domains, IReadOnlyList<string> Topics, string? Timeframe) Describe(PostEnvelope envelope)
    {
        var small = envelope.Tags.Where(SelectorRules.IsAnswerTag).Select(tag => tag.Tag).ToList();
        return (Values(small, PostTagsConstants.TypeTagPrefix).FirstOrDefault(), SelectorRules.MostSpecific(Values(small, PostTagsConstants.DomainTagPrefix)),
            [.. Values(small, PostTagsConstants.TopicTagPrefix).Distinct(StringComparer.Ordinal)], Values(small, PostTagsConstants.TimeframeTagPrefix).FirstOrDefault());
    }

    private static IEnumerable<string> Values(IEnumerable<string> tags, string prefix) =>
        tags.Where(tag => tag.StartsWith(prefix, StringComparison.Ordinal)).Select(tag => tag[prefix.Length..]);

    private static string? TitleEn(PostEnvelope envelope) =>
        envelope.Extras.TryGetValue(PostTagsConstants.ExtrasNamespace, out var tags) && tags.ValueKind == JsonValueKind.Object
            && tags.TryGetProperty("titleEn", out var title) && title.ValueKind == JsonValueKind.String ? title.GetString() : null;

    /// <summary>Replaces the contacts in a title and cuts it to <see cref="MaxTitleLength"/> characters without splitting a surrogate pair.</summary>
    private static string? Clean(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        string scrubbed = PostExtractionValidation.Scrub(title.Trim());
        return scrubbed.Length <= MaxTitleLength ? scrubbed : scrubbed[..(char.IsHighSurrogate(scrubbed[MaxTitleLength - 1]) ? MaxTitleLength - 1 : MaxTitleLength)];
    }

    private DateTimeOffset Now()
    {
        var now = time.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
    }

    /// <summary>Reads each source of the container as <c>{family}/{handle}</c>.</summary>
    private static async Task<IReadOnlyDictionary<Guid, string>> ReadSourceNamesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid containerId, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        await using var command = PostSql.Command(connection, transaction, "SELECT id,family,handle FROM harvest_sources WHERE container_id=@container",
            ("container", containerId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            names[reader.GetGuid(0)] = $"{reader.GetString(1)}/{reader.GetString(2)}";
        return names;
    }

    /// <summary>Reads the live links of every post the diff window holds, in the order a post lists them.</summary>
    private static async Task<IReadOnlyDictionary<Guid, List<SelectorLink>>> ReadWindowLinksAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid containerId, DateTimeOffset since, IReadOnlyDictionary<Guid, Guid?> groups, CancellationToken ct)
    {
        var links = new Dictionary<Guid, List<SelectorLink>>();
        await using var command = PostSql.Command(connection, transaction, """
            SELECT l.post_id,l.id,l.source_id,l.url FROM harvest_post_links l
            JOIN harvest_posts p ON p.container_id=l.container_id AND p.id=l.post_id
            WHERE l.container_id=@container AND l.retired_at IS NULL AND p.merged_into IS NULL AND p.purged_at IS NULL AND p.last_changed_at>=@since
            ORDER BY l.post_id,l.first_seen_at,l.id
            """, ("container", containerId), ("since", since));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var postId = reader.GetGuid(0);
            var sourceId = reader.GetGuid(2);
            if (!links.TryGetValue(postId, out var list)) links[postId] = list = [];
            list.Add(new SelectorLink(reader.GetGuid(1), sourceId, groups.GetValueOrDefault(sourceId), reader.GetString(3)));
        }
        return links;
    }

    private static async Task<SubscriptionRow?> ReadSubscriptionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken ct)
    {
        await using var command = PostSql.Command(connection, transaction, """
            SELECT id,tenant_id,container_id,kind,state,name,selector::text,selector_version,schema,context::text
            FROM harvest_subscriptions WHERE id=@id
            """, ("id", id));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return !await reader.ReadAsync(ct) ? null : new SubscriptionRow(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : SelectorStore.ReadSelector(reader.GetString(6)),
            reader.IsDBNull(7) ? null : reader.GetInt32(7), reader.IsDBNull(8) ? null : reader.GetString(8),
            StoreJson.Read(reader.GetString(9), new Dictionary<string, string>()));
    }

    private sealed record SubscriptionRow(Guid Id, Guid TenantId, Guid ContainerId, string Kind, string State, string? Name, SubscriptionSelector? Selector,
        int? Version, string? Schema, IReadOnlyDictionary<string, string> Context)
    {
        internal string Label => Name ?? Id.ToString();
    }

    private static Result<T> Invalid<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.Validation(message));
    private static Result<T> Conflict<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.Conflict(message));
    private static Result<T> Missing<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.NotFound(message));
}
