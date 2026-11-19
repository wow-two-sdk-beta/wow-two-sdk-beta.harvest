using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Represents an active selector subscription of a container, with what its log holds about one post.</summary>
/// <param name="Id">The subscription's identifier.</param>
/// <param name="Version">The version of <paramref name="Selector"/>.</param>
/// <param name="Selector">The stored, normalized selector.</param>
/// <param name="Schema">The consumer schema the subscription asked for.</param>
/// <param name="Context">The subscription's own routing context.</param>
/// <param name="Seen">Whether the log holds any event of the post.</param>
/// <param name="State">The post state the log's newest event of the post carried, or null when none.</param>
/// <param name="Unmatched">Whether the log's newest content event of the post is <c>post.unmatched</c>.</param>
internal sealed record SelectorSubscription(Guid Id, int Version, SubscriptionSelector Selector, string? Schema,
    IReadOnlyDictionary<string, string> Context, bool Seen, string? State, bool Unmatched);

/// <summary>Represents what a selector needs of the store to judge a post: the group of each source and the pipelines its
/// section refs name.</summary>
/// <param name="Groups">The source group of each source of the container, null for an ungrouped source.</param>
/// <param name="Sections">The referenced pipelines, by pipeline id.</param>
internal sealed record SelectorScope(IReadOnlyDictionary<Guid, Guid?> Groups, IReadOnlyDictionary<Guid, SelectorSection> Sections);

/// <summary>Represents a post a selector change re-judges, with what the subscription's log holds about it.</summary>
/// <param name="PostId">The post.</param>
/// <param name="Seen">Whether the log holds any event of the post.</param>
/// <param name="Unmatched">Whether the log's newest content event of the post is <c>post.unmatched</c>.</param>
internal sealed record SelectorCandidate(Guid PostId, bool Seen, bool Unmatched);

/// <summary>Represents the verdict as <c>harvest_subscription_event.matched</c> stores it.</summary>
/// <param name="SelectorVersion">The version of the selector that judged the revision.</param>
/// <param name="Verdict">What the selector said.</param>
/// <param name="Clauses">The clauses behind the verdict.</param>
/// <param name="Context">The merged context the event's payload renders as its top-level <c>context</c>.</param>
internal sealed record StoredMatch(int SelectorVersion, SelectorVerdict Verdict, IReadOnlyList<string> Clauses,
    IReadOnlyDictionary<string, string> Context);

/// <summary>Reads and writes what selector subscriptions need of the store, inside the caller's transaction.</summary>
internal static class SelectorStore
{
    /// <summary>Reads the container's active selector subscriptions with, for one post, the same <c>previous</c> and
    /// <c>revision</c> laterals <see cref="DeliveryEventWriter.AppendAsync"/> reads for a channel.</summary>
    internal static async Task<IReadOnlyList<SelectorSubscription>> ReadSelectorsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid containerId, Guid postId, CancellationToken ct)
    {
        var selectors = new List<SelectorSubscription>();
        await using var command = PostSql.Command(connection, transaction, """
            SELECT s.id,s.selector_version,s.selector::text,s.schema,s.context::text,previous.id IS NOT NULL,previous.payload->>'state',
                COALESCE(revision.type='post.unmatched',false)
            FROM harvest_subscriptions s
            LEFT JOIN LATERAL (SELECT e.id,e.payload FROM harvest_subscription_event e
                WHERE e.subscription_id=s.id AND e.post_id=@post ORDER BY e.id DESC LIMIT 1) previous ON true
            LEFT JOIN LATERAL (SELECT e.type FROM harvest_subscription_event e
                WHERE e.subscription_id=s.id AND e.post_id=@post AND e.type IN ('post.new','post.changed','post.extracted','post.unmatched')
                ORDER BY e.id DESC LIMIT 1) revision ON true
            WHERE s.container_id=@container AND s.kind='selector' AND s.state='active'
            ORDER BY s.created_at,s.id
            """, ("container", containerId), ("post", postId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            selectors.Add(new SelectorSubscription(reader.GetGuid(0), reader.GetInt32(1), ReadSelector(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3), StoreJson.Read(reader.GetString(4), new Dictionary<string, string>()),
                reader.GetBoolean(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetBoolean(7)));
        return selectors;
    }

    /// <summary>Reads the container's posts a change window re-judges: every live post changed since
    /// <paramref name="since"/>, and every post the subscription's log holds an event of, merged and purged posts aside.
    /// At most <paramref name="limit"/> rows, so a caller sees that it was exceeded by asking for one more.</summary>
    internal static async Task<IReadOnlyList<SelectorCandidate>> ReadWindowAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid containerId, Guid subscriptionId, DateTimeOffset since, int limit, CancellationToken ct)
    {
        var candidates = new List<SelectorCandidate>();
        await using var command = PostSql.Command(connection, transaction, """
            SELECT p.id,previous.id IS NOT NULL,COALESCE(revision.type='post.unmatched',false)
            FROM harvest_posts p
            LEFT JOIN LATERAL (SELECT e.id FROM harvest_subscription_event e
                WHERE e.subscription_id=@subscription AND e.post_id=p.id ORDER BY e.id DESC LIMIT 1) previous ON true
            LEFT JOIN LATERAL (SELECT e.type FROM harvest_subscription_event e
                WHERE e.subscription_id=@subscription AND e.post_id=p.id AND e.type IN ('post.new','post.changed','post.extracted','post.unmatched')
                ORDER BY e.id DESC LIMIT 1) revision ON true
            WHERE p.container_id=@container AND p.merged_into IS NULL AND p.purged_at IS NULL
              AND (previous.id IS NOT NULL OR (p.state='live' AND p.last_changed_at>=@since))
            ORDER BY p.first_seen_at,p.id LIMIT @limit
            """, ("container", containerId), ("subscription", subscriptionId), ("since", since.ToUniversalTime()), ("limit", limit));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            candidates.Add(new SelectorCandidate(reader.GetGuid(0), reader.GetBoolean(1), reader.GetBoolean(2)));
        return candidates;
    }

    /// <summary>Reads the group of each source of the container and the section each referenced pipeline names.</summary>
    internal static async Task<SelectorScope> ReadScopeAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid containerId,
        IEnumerable<SubscriptionSelector> selectors, CancellationToken ct)
    {
        var groups = new Dictionary<Guid, Guid?>();
        await using (var command = PostSql.Command(connection, transaction, "SELECT id,group_id FROM harvest_sources WHERE container_id=@container",
                         ("container", containerId)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                groups[reader.GetGuid(0)] = reader.IsDBNull(1) ? null : reader.GetGuid(1);

        var pipelines = selectors.SelectMany(Refs).Where(reference => reference.PipelineId.HasValue).Select(reference => reference.PipelineId!.Value)
            .Distinct().ToArray();
        var sections = new Dictionary<Guid, SelectorSection>();
        if (pipelines.Length > 0)
        {
            await using var command = PostSql.Command(connection, transaction,
                "SELECT id,source_id,scope::text FROM harvest_pipelines WHERE container_id=@container AND id=ANY(@ids)",
                ("container", containerId), ("ids", pipelines));
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                sections[reader.GetGuid(0)] = new SelectorSection(reader.GetGuid(0), reader.GetGuid(1),
                    StoreJson.Read(reader.GetString(2), new PipelineScope()));
        }
        return new SelectorScope(groups, sections);
    }

    /// <summary>Returns what a selector judges of a post revision: its envelope and its links in the post's order, each with
    /// the group of its source. The context link a selector prefers is the one that supplied the post's title.</summary>
    internal static SelectorPostFacts Facts(PostModel post, IReadOnlyDictionary<Guid, Guid?> groups) => new()
    {
        Envelope = post.Base,
        Links = [.. post.Links.Select(link => new SelectorLink(link.Id, link.SourceId, groups.GetValueOrDefault(link.SourceId), link.Url))],
        PrimaryLinkId = post.FieldSources.TryGetValue("title", out var titleLink) ? titleLink : null,
    };

    /// <summary>Returns the json an event stores in <c>matched</c>: the selector version, the verdict, the clauses and the
    /// merged context.</summary>
    internal static string Matched(int version, SelectorEvaluation evaluation) =>
        StoreJson.Write(new StoredMatch(version, evaluation.Verdict, evaluation.Clauses, evaluation.Context));

    /// <summary>Returns the problem in a normalized selector, or null when it is valid: the limits and forms of
    /// <see cref="SelectorRules.Validate"/>, then every domain key an active taxonomy node, and every source ref a group,
    /// source or pipeline of the container.</summary>
    internal static async Task<string?> ValidateAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid containerId,
        SubscriptionSelector selector, CancellationToken ct)
    {
        if (SelectorRules.Validate(selector) is { } problem)
            return problem;

        var keys = selector.Domains.Select(domain => domain.Key).Concat(selector.Domains.SelectMany(domain => domain.Except))
            .Concat(selector.ExcludeDomains).Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length > 0)
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            await using var command = PostSql.Command(connection, transaction,
                "SELECT key FROM harvest_taxonomy_nodes WHERE active AND key = ANY(@keys)", ("keys", keys));
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                known.Add(reader.GetString(0));
            if (keys.FirstOrDefault(key => !known.Contains(key)) is { } unknown)
                return $"Domain key '{unknown}' is not an active taxonomy node.";
        }

        var refs = Refs(selector).ToList();
        foreach (var (table, label, ids) in new[]
                 {
                     ("harvest_source_groups", "Source group", refs.Where(reference => reference.GroupId.HasValue).Select(reference => reference.GroupId!.Value)),
                     ("harvest_sources", "Source", refs.Where(reference => reference.SourceId.HasValue).Select(reference => reference.SourceId!.Value)),
                     ("harvest_pipelines", "Pipeline", refs.Where(reference => reference.PipelineId.HasValue).Select(reference => reference.PipelineId!.Value)),
                 })
        {
            var wanted = ids.Distinct().ToArray();
            if (wanted.Length == 0)
                continue;
            var found = new HashSet<Guid>();
            await using var command = PostSql.Command(connection, transaction, $"SELECT id FROM {table} WHERE container_id=@container AND id=ANY(@ids)",
                ("container", containerId), ("ids", wanted));
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                found.Add(reader.GetGuid(0));
            foreach (var id in wanted)
                if (!found.Contains(id))
                    return $"{label} {id} is not in the subscription's container.";
        }
        return null;
    }

    /// <summary>Reads a stored selector.</summary>
    internal static SubscriptionSelector ReadSelector(string json) => StoreJson.Read(json, new SubscriptionSelector());

    private static IEnumerable<SelectorSourceRef> Refs(SubscriptionSelector selector) =>
        (selector.Sources?.Include ?? []).Concat(selector.Sources?.Exclude ?? []);
}
