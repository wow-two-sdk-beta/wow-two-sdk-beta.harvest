using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Appends a post revision to interested subscriptions inside the post writer's transaction.</summary>
internal static class DeliveryEventWriter
{
    internal static async Task AppendAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, PostModel post, string type, Guid? runId, Guid? linkId, DateTimeOffset at, DeliveryPostFilters filters, CancellationToken ct)
    {
        var subscriptions = new List<(Guid Id, string? FilterKey, bool Seen, string? State, bool Unmatched, Guid SourceId, PipelineScope Scope, string? Received)>();
        await using (var command = PostSql.Command(connection, transaction, """
                         SELECT s.id,s.filter_key,previous.id IS NOT NULL,previous.payload->>'state',COALESCE(revision.type='post.unmatched',false),
                             p.source_id,p.scope::text,CASE WHEN CAST(@changed AS boolean) THEN previous.payload->>'base' END
                         FROM harvest_subscriptions s JOIN harvest_pipelines p ON p.id=s.pipeline_id
                         LEFT JOIN LATERAL (SELECT e.id,e.payload FROM harvest_subscription_event e
                             WHERE e.subscription_id=s.id AND e.post_id=@post ORDER BY e.id DESC LIMIT 1) previous ON true
                         LEFT JOIN LATERAL (SELECT e.type FROM harvest_subscription_event e
                             WHERE e.subscription_id=s.id AND e.post_id=@post AND e.type IN ('post.new','post.changed','post.extracted','post.unmatched')
                             ORDER BY e.id DESC LIMIT 1) revision ON true
                         WHERE s.container_id=@container AND s.state='active' AND (
                             (CAST(@run AS uuid) IS NOT NULL AND p.id=(SELECT pipeline_id FROM harvest_runs WHERE id=@run))
                             OR (CAST(@run AS uuid) IS NULL AND EXISTS(SELECT 1 FROM harvest_post_links l WHERE
                                 (l.post_id=@post OR l.post_id=CAST(@merged AS uuid)) AND l.source_id=p.source_id AND l.retired_at IS NULL))
                             OR previous.id IS NOT NULL)
                         """, ("container", post.ContainerId), ("run", runId), ("post", post.Id), ("merged", post.MergedInto),
                         ("changed", type == DeliveryEventTypes.PostChanged)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) subscriptions.Add((reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetBoolean(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetBoolean(4), reader.GetGuid(5),
                StoreJson.Read(reader.GetString(6), new PipelineScope()), reader.IsDBNull(7) ? null : reader.GetString(7)));
        foreach (var subscription in subscriptions)
        {
            bool withinSection = !subscription.Scope.RestrictToPath
                || post.Links.Any(link => link.SourceId == subscription.SourceId && subscription.Scope.IncludesUrl(link.Url));
            var verdict = withinSection ? filters.Evaluate(subscription.FilterKey, post.Base) : false;
            bool stateChanged = subscription.State != post.State.ToString().ToLowerInvariant();
            if (verdict == true && !stateChanged && !subscription.Unmatched && subscription.Received is { } received
                && filters.IgnoresTagOnlyChange(subscription.FilterKey) && TagOnlyChange.Is(received, post.Base))
                continue;
            if (Deliverable(type, verdict, subscription.Seen, subscription.Unmatched, stateChanged) is { } delivered)
                await AppendOneAsync(connection, transaction, subscription.Id, post, delivered, runId, linkId, "ingest", at, ct);
        }
        await AppendSelectorsAsync(connection, transaction, post, type, runId, linkId, at, ct);
    }

    /// <summary>Appends a done extraction answer to the subscriptions that already hold the post, as <c>post.extracted</c>:
    /// the post's own snapshot at its current revision with the answer added to its <c>base.extras</c>, which the stored post
    /// never holds.</summary>
    /// <remarks>
    ///   - a channel subscription gets it only when it already holds the post (an earlier <c>post.new</c>, <c>post.changed</c> or
    ///     <c>post.extracted</c>) and the post passes its section and filter now; an absent or throwing filter, or a failing
    ///     one, sends nothing, so an answer never raises <c>post.unmatched</c>
    ///   - a selector subscription judges the snapshot as it judges any content write (<see cref="AppendSelectorsAsync"/>)
    ///   - the item suffix joins the item hash, so one post revision holds one event per answer: a replay of the same answer
    ///     writes nothing, a re-run by another prompt version or model is a new event
    /// </remarks>
    internal static async Task AppendExtractedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, PostModel post, string extrasKey,
        JsonElement extras, string itemSuffix, DateTimeOffset at, DeliveryPostFilters filters, CancellationToken ct)
    {
        var carrying = post with
        {
            Base = post.Base with
            {
                Extras = new Dictionary<string, JsonElement>(post.Base.Extras) { [extrasKey] = extras },
            },
        };
        var holders = new List<(Guid Id, string? FilterKey, Guid SourceId, PipelineScope Scope)>();
        await using (var command = PostSql.Command(connection, transaction, """
                         SELECT s.id,s.filter_key,p.source_id,p.scope::text
                         FROM harvest_subscriptions s JOIN harvest_pipelines p ON p.id=s.pipeline_id
                         WHERE s.container_id=@container AND s.state='active' AND EXISTS(
                             SELECT 1 FROM harvest_subscription_event e WHERE e.subscription_id=s.id AND e.post_id=@post
                                 AND e.type IN ('post.new','post.changed','post.extracted'))
                         ORDER BY s.created_at,s.id
                         """, ("container", post.ContainerId), ("post", post.Id)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) holders.Add((reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetGuid(2), StoreJson.Read(reader.GetString(3), new PipelineScope())));
        foreach (var holder in holders)
        {
            bool withinSection = !holder.Scope.RestrictToPath
                || carrying.Links.Any(link => link.SourceId == holder.SourceId && holder.Scope.IncludesUrl(link.Url));
            if (withinSection && filters.Evaluate(holder.FilterKey, carrying.Base) == true)
                await AppendOneAsync(connection, transaction, holder.Id, carrying, DeliveryEventTypes.PostExtracted, null, null, "ingest", at, ct,
                    itemSuffix: itemSuffix);
        }
        await AppendSelectorsAsync(connection, transaction, carrying, DeliveryEventTypes.PostExtracted, null, null, at, ct, itemSuffix);
    }

    /// <summary>Appends a post write to the container's selector subscriptions: each judges the revision's own envelope
    /// and <see cref="SubscriptionSelectorMatcher.Choose"/> names what its log receives, with the verdict beside it.</summary>
    /// <remarks>A selector subscription follows tags and sources, not one pipeline, so every write of the container reaches
    /// it whatever run caused it. A post the selector cannot judge yet is pending: no event, until the tags' own
    /// <c>post.changed</c> arrives.</remarks>
    private static async Task AppendSelectorsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, PostModel post, string type, Guid? runId, Guid? linkId,
        DateTimeOffset at, CancellationToken ct, string? itemSuffix = null)
    {
        var selectors = await SelectorStore.ReadSelectorsAsync(connection, transaction, post.ContainerId, post.Id, ct);
        if (selectors.Count == 0)
            return;
        var scope = await SelectorStore.ReadScopeAsync(connection, transaction, post.ContainerId, selectors.Select(selector => selector.Selector), ct);
        var facts = SelectorStore.Facts(post, scope.Groups);
        string state = post.State.ToString().ToLowerInvariant();
        foreach (var selector in selectors)
        {
            var evaluation = SubscriptionSelectorMatcher.Evaluate(selector.Selector, facts, scope.Sections, selector.Schema, selector.Context);
            if (SubscriptionSelectorMatcher.Choose(type, evaluation.Verdict, selector.Seen, selector.Unmatched, selector.State != state) is { } delivered)
                await AppendOneAsync(connection, transaction, selector.Id, post, delivered, runId, linkId, "ingest", at, ct,
                    SelectorStore.Matched(selector.Version, evaluation), itemSuffix);
        }
    }

    /// <summary>Chooses what one subscription receives for a post write, or null for nothing.</summary>
    /// <param name="type">The write's own type.</param>
    /// <param name="verdict">Whether the revision passes the subscription's section and filter; null when an absent or
    /// throwing filter cannot judge it.</param>
    /// <param name="seen">Whether the subscription already holds the post.</param>
    /// <param name="unmatched">Whether the subscription's newest revision record for the post is <c>post.unmatched</c>.</param>
    /// <param name="stateChanged">Whether the post's state differs from the subscription's newest record.</param>
    /// <remarks>A passing revision always goes out. A held post keeps its lifecycle events. Its first revision that
    /// fails goes out once as <c>post.unmatched</c>; later failing revisions are withheld, a restore from gone included,
    /// until one passes again as its own type. An undecided filter neither unmatches nor admits, but keeps a held post's
    /// state changes.</remarks>
    internal static string? Deliverable(string type, bool? verdict, bool seen, bool unmatched, bool stateChanged) =>
        verdict == true ? type
        : !seen ? null
        : DeliveryPostFilters.IsLifecycle(type) ? type
        : unmatched ? null
        : verdict == false ? DeliveryEventTypes.PostUnmatched
        : stateChanged ? type
        : null;

    /// <summary>Appends one event of a post revision to a subscription's log; a repeat of the same item and type writes nothing.</summary>
    /// <remarks>A selector subscription's event carries its verdict json (<see cref="SelectorStore.Matched"/>) in <c>matched</c>.
    /// An item suffix such as <c>:s2</c>, a selector change's version, joins the item hash input so a second unmatch or
    /// re-match at the same revision stays a distinct item.</remarks>
    internal static Task<int> AppendOneAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid subscriptionId, PostModel post, string type, Guid? runId, Guid? linkId, string reason, DateTimeOffset at, CancellationToken ct,
        string? matched = null, string? itemSuffix = null) =>
        PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_subscription_event(subscription_id,event_id,item_id,type,post_id,post_key,revision,link_id,run_id,reason,occurred_at,payload,matched)
            VALUES(@subscription,@event,@item,@type,@post,@key,@revision,@link,@run,@reason,@at,CAST(@payload AS jsonb),CAST(@matched AS jsonb))
            ON CONFLICT(subscription_id,item_id,type) DO NOTHING
            """, ct, ("subscription", subscriptionId), ("event", Guid.CreateVersion7(at)),
            ("item", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{post.Id:N}:{post.Revision}:{type}{itemSuffix}"))).ToLowerInvariant()),
            ("type", type), ("post", post.Id), ("key", post.Id.ToString("N")), ("revision", post.Revision), ("link", linkId),
            ("run", runId), ("reason", reason), ("at", at), ("payload", StoreJson.Write(post)), ("matched", matched));
}
