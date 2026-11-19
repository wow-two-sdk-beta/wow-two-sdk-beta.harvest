using System.Globalization;
using System.Text.Json;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Represents one entity a search keeps, before its years and counts are read.</summary>
internal sealed record EntityHit(Guid Id, string EntityType, string Name, string[] Aliases, string? PrimaryDomain, string Status, string Cadence,
    DateTimeOffset LastSeenAt, decimal? Score);

/// <summary>Represents an entity's counts across its occurrences and surviving posts.</summary>
internal sealed record EntityCounts(long Occurrences, DateOnly? Predicted, long Posts, long Sources);

/// <summary>Holds the read SQL of <see cref="IEntitiesBroker"/> over the entity layer's tables and the change feed
/// (<c>harvest_entity_events</c>). A bound post counts while it survives: neither merged into another post nor purged.</summary>
internal static class EntityReadStore
{
    /// <summary>Joins a relation <c>r</c> to its surviving post <c>p</c>.</summary>
    private const string LivePost = "JOIN harvest_posts p ON p.id=r.post_id AND p.merged_into IS NULL AND p.purged_at IS NULL";

    /// <summary>The year a relation is about: its occurrence's start year (<c>o</c>, left-joined), else the year the post
    /// says it was posted, else the year it was first seen.</summary>
    private const string PostYear = """
        COALESCE(CAST(extract(year FROM o.starts) AS integer),
            CASE WHEN p.base->'postedAt'->>'value' ~ '^[0-9]{4}' THEN CAST(left(p.base->'postedAt'->>'value',4) AS integer) END,
            CAST(extract(year FROM p.first_seen_at AT TIME ZONE 'UTC') AS integer))
        """;

    /// <summary>The domain filter over a primary-domain column: equal, or under it.</summary>
    private const string DomainFilter =
        "(CAST(@domain AS text) IS NULL OR {0}=@domain OR left({0},length(@domain)+1)=@domain || '/')";

    private const string OccurrenceColumns = "o.id,o.label,o.starts,o.ends,o.precision,o.certainty,o.predicted,o.status,o.place::text";

    /// <summary>Whether the container exists.</summary>
    internal static async Task<bool> ContainerExistsAsync(NpgsqlConnection connection, Guid containerId, CancellationToken ct) =>
        await PostSql.ScalarAsync(connection, null, "SELECT 1 FROM harvest_containers WHERE id=@container", ct, ("container", containerId)) is not null;

    /// <summary>Reads an entity of the container, following merges to the one that stays; null when the container holds
    /// no such entity.</summary>
    internal static async Task<EntityRow?> ResolveAsync(NpgsqlConnection connection, Guid containerId, Guid entityId, CancellationToken ct)
    {
        var entity = await EntityStore.ReadEntityAsync(connection, null, entityId, ct);
        for (int hop = 0; entity?.MergedInto is { } into && hop < 10; hop++)
            entity = await EntityStore.ReadEntityAsync(connection, null, into, ct);
        return entity is { MergedInto: null } && entity.ContainerId == containerId ? entity : null;
    }

    /// <summary>Finds the container's open entities by name and alias similarity, best first; without text, the newest
    /// the filters keep.</summary>
    internal static async Task<List<EntityHit>> SearchAsync(NpgsqlConnection connection, Guid containerId, IReadOnlyList<string> keys, string? text,
        string? entityType, string? domain, int limit, CancellationToken ct)
    {
        var hits = new List<EntityHit>();
        await using var command = PostSql.Command(connection, null, $"""
            SELECT * FROM (
                SELECT e.id,e.entity_type,e.name,e.aliases,e.primary_domain,e.status,e.cadence,e.last_seen_at,
                    CASE WHEN CAST(@text AS text) IS NULL THEN NULL ELSE GREATEST(
                        COALESCE((SELECT max(GREATEST(similarity(e.name_key,k),word_similarity(k,e.name_key))) FROM unnest(CAST(@keys AS text[])) k),0),
                        COALESCE((SELECT max(GREATEST(similarity(a,k),word_similarity(k,a))) FROM unnest(e.alias_keys) a,unnest(CAST(@keys AS text[])) k),0),
                        similarity(lower(e.name),lower(CAST(@text AS text))),
                        COALESCE((SELECT max(similarity(lower(a),lower(CAST(@text AS text)))) FROM unnest(e.aliases) a),0)) END AS score
                FROM harvest_entities e
                WHERE e.container_id=@container AND e.merged_into IS NULL AND (CAST(@type AS text) IS NULL OR e.entity_type=@type)
                  AND {string.Format(CultureInfo.InvariantCulture, DomainFilter, "e.primary_domain")}) h
            WHERE h.score IS NULL OR h.score>=0.35
            ORDER BY h.score DESC NULLS LAST,h.last_seen_at DESC,h.id LIMIT @limit
            """, ("keys", keys.ToArray()), ("text", text), ("container", containerId), ("type", entityType), ("domain", domain), ("limit", limit));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            hits.Add(new EntityHit(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<string[]>(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : Math.Round((decimal)reader.GetFloat(8), 3)));
        return hits;
    }

    /// <summary>Reads the years each entity has an occurrence or a surviving post in, earliest first.</summary>
    internal static async Task<Dictionary<Guid, List<EntityYearSummaryModel>>> YearsAsync(NpgsqlConnection connection, IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        var years = ids.ToDictionary(id => id, _ => new List<EntityYearSummaryModel>());
        if (ids.Count == 0)
            return years;
        await using var command = PostSql.Command(connection, null, $"""
            SELECT entity_id,year,CAST(sum(occurrences) AS bigint),bool_or(predicted),CAST(sum(posts) AS bigint) FROM (
                SELECT o.entity_id,CAST(extract(year FROM o.starts) AS integer) AS year,count(*) FILTER (WHERE NOT o.predicted) AS occurrences,
                    bool_or(o.predicted) AS predicted,CAST(0 AS bigint) AS posts
                FROM harvest_entity_occurrences o WHERE o.entity_id=ANY(@ids) GROUP BY 1,2
                UNION ALL
                SELECT r.entity_id,{PostYear},CAST(0 AS bigint),false,count(*)
                FROM harvest_entity_relations r {LivePost} LEFT JOIN harvest_entity_occurrences o ON o.id=r.occurrence_id
                WHERE r.entity_id=ANY(@ids) GROUP BY 1,2) y
            GROUP BY 1,2 ORDER BY 1,2
            """, ("ids", ids.ToArray()));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            years[reader.GetGuid(0)].Add(new EntityYearSummaryModel
            {
                Year = reader.GetInt32(1), Occurrences = reader.GetInt64(2), Predicted = reader.GetBoolean(3), Posts = reader.GetInt64(4),
            });
        return years;
    }

    /// <summary>Counts each entity's real occurrences, predicted next start, surviving posts and their sources.</summary>
    internal static async Task<Dictionary<Guid, EntityCounts>> CountsAsync(NpgsqlConnection connection, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var counts = new Dictionary<Guid, EntityCounts>();
        if (ids.Count == 0)
            return counts;
        await using var command = PostSql.Command(connection, null, $"""
            SELECT e.id,
                (SELECT count(*) FROM harvest_entity_occurrences o WHERE o.entity_id=e.id AND NOT o.predicted),
                (SELECT min(o.starts) FROM harvest_entity_occurrences o WHERE o.entity_id=e.id AND o.predicted),
                (SELECT count(*) FROM harvest_entity_relations r {LivePost} WHERE r.entity_id=e.id),
                (SELECT count(DISTINCT l.source_id) FROM harvest_entity_relations r {LivePost}
                    JOIN harvest_post_links l ON l.post_id=r.post_id WHERE r.entity_id=e.id)
            FROM unnest(CAST(@ids AS uuid[])) e(id)
            """, ("ids", ids.ToArray()));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            counts[reader.GetGuid(0)] = new EntityCounts(reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2),
                reader.GetInt64(3), reader.GetInt64(4));
        return counts;
    }

    /// <summary>Reads one page of an entity's surviving posts, newest posted first, after a position.</summary>
    /// <returns>Up to <paramref name="limit"/> posts with each one's sort key.</returns>
    internal static async Task<List<(EntityPostModel Post, string SortKey)>> PostsAsync(NpgsqlConnection connection, Guid entityId, int? year,
        string? role, int limit, string? afterKey, Guid afterPost, CancellationToken ct)
    {
        var posts = new List<(EntityPostModel, string)>();
        await using var command = PostSql.Command(connection, null, $"""
            SELECT * FROM (
                SELECT r.post_id,r.role,r.changes,r.occurrence_id,{PostYear} AS year,p.base->>'title' AS title,l.url,l.source_id,
                    COALESCE(s.display_name,s.handle) AS source,p.base->'postedAt'->>'value' AS posted,p.first_seen_at,r.created_at,
                    COALESCE(NULLIF(p.base->'postedAt'->>'value',''),to_char(p.first_seen_at AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI:SS'))
                        COLLATE "C" AS sort_key
                FROM harvest_entity_relations r {LivePost}
                LEFT JOIN harvest_entity_occurrences o ON o.id=r.occurrence_id
                LEFT JOIN LATERAL (SELECT l.source_id,l.url FROM harvest_post_links l WHERE l.post_id=p.id
                    ORDER BY COALESCE(l.id::text=COALESCE(p.field_prov->>'title',p.field_prov->>'text'),false) DESC,l.retired_at IS NULL DESC,
                        l.first_seen_at,l.id LIMIT 1) l ON true
                LEFT JOIN harvest_sources s ON s.id=l.source_id
                WHERE r.entity_id=@entity AND (CAST(@role AS text) IS NULL OR r.role=@role)) x
            WHERE (CAST(@year AS integer) IS NULL OR x.year=@year)
              AND (CAST(@key AS text) IS NULL OR (x.sort_key,x.post_id) < (CAST(@key AS text) COLLATE "C",@post))
            ORDER BY x.sort_key DESC,x.post_id DESC LIMIT @limit
            """, ("entity", entityId), ("role", role), ("year", year), ("key", afterKey), ("post", afterPost), ("limit", limit));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            posts.Add((new EntityPostModel
            {
                PostId = reader.GetGuid(0), Role = reader.GetString(1), Changes = reader.GetFieldValue<string[]>(2),
                OccurrenceId = reader.IsDBNull(3) ? null : reader.GetGuid(3), Year = reader.GetInt32(4), Title = Text(reader, 5),
                Link = Text(reader, 6), SourceId = reader.IsDBNull(7) ? null : reader.GetGuid(7), Source = Text(reader, 8),
                PostedAt = Text(reader, 9), FirstSeenAtUtc = reader.GetFieldValue<DateTimeOffset>(10), BoundAtUtc = reader.GetFieldValue<DateTimeOffset>(11),
            }, reader.GetString(12)));
        return posts;
    }

    /// <summary>Reads the occurrences of the container's open entities that overlap a window, earliest first.</summary>
    internal static async Task<List<EntityCalendarEntryModel>> CalendarAsync(NpgsqlConnection connection, Guid containerId, DateOnly from, DateOnly to,
        string? entityType, string? domain, CancellationToken ct)
    {
        var entries = new List<EntityCalendarEntryModel>();
        await using var command = PostSql.Command(connection, null, $"""
            SELECT {OccurrenceColumns},e.id,e.entity_type,e.name,e.primary_domain,e.cadence,
                (SELECT count(*) FROM harvest_entity_relations r {LivePost} WHERE r.occurrence_id=o.id)
            FROM harvest_entity_occurrences o JOIN harvest_entities e ON e.id=o.entity_id
            WHERE e.container_id=@container AND e.merged_into IS NULL AND (CAST(@type AS text) IS NULL OR e.entity_type=@type)
              AND {string.Format(CultureInfo.InvariantCulture, DomainFilter, "e.primary_domain")}
              AND o.starts<=@to
              AND COALESCE(o.ends,CASE o.precision
                  WHEN 'year' THEN make_date(CAST(extract(year FROM o.starts) AS integer),12,31)
                  WHEN 'month' THEN CAST(date_trunc('month',CAST(o.starts AS timestamp)) + interval '1 month' - interval '1 day' AS date)
                  ELSE o.starts END)>=@from
            ORDER BY o.starts,e.name,o.id
            """, ("container", containerId), ("type", entityType), ("domain", domain), ("from", from), ("to", to));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            entries.Add(new EntityCalendarEntryModel
            {
                Occurrence = Occurrence(reader), EntityId = reader.GetGuid(9), EntityType = reader.GetString(10), Name = reader.GetString(11),
                PrimaryDomain = Text(reader, 12), Cadence = reader.GetString(13), Posts = reader.GetInt64(14),
            });
        return entries;
    }

    /// <summary>Reads a container's committed change events below the snapshot horizon after a position, in log order.</summary>
    /// <returns>Up to <paramref name="limit"/> events with each one's position.</returns>
    internal static async Task<List<(ulong TransactionId, long RowId, EntityEvent Event)>> EventsAsync(NpgsqlConnection connection, Guid containerId,
        ulong afterTransaction, long afterRow, int limit, string? entityType, string? domain, CancellationToken ct)
    {
        var events = new List<(ulong, long, EntityEvent)>();
        await using var command = PostSql.Command(connection, null, $"""
            SELECT e.tx::text,e.id,e.event_id,e.type,e.occurred_at,e.entity_id,e.entity_type,e.domain,e.occurrence_id,e.post_id,e.payload::text
            FROM harvest_entity_events e
            WHERE e.container_id=@container AND e.tx < pg_snapshot_xmin(pg_current_snapshot())
              AND (e.tx,e.id) > (CAST(@tx AS xid8),@row)
              AND (CAST(@type AS text) IS NULL OR e.entity_type=@type)
              AND {string.Format(CultureInfo.InvariantCulture, DomainFilter, "e.domain")}
            ORDER BY e.tx,e.id LIMIT @limit
            """, ("container", containerId), ("tx", afterTransaction.ToString(CultureInfo.InvariantCulture)), ("row", afterRow), ("type", entityType),
            ("domain", domain), ("limit", limit));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var payload = JsonSerializer.Deserialize<EventPayload>(reader.GetString(10), EntityJson.Options) ?? new EventPayload();
            events.Add((ulong.Parse(reader.GetString(0), CultureInfo.InvariantCulture), reader.GetInt64(1), new EntityEvent
            {
                EventId = reader.GetGuid(2), Type = reader.GetString(3), OccurredAtUtc = reader.GetFieldValue<DateTimeOffset>(4), ContainerId = containerId,
                EntityId = reader.GetGuid(5), EntityType = reader.GetString(6), PrimaryDomain = Text(reader, 7),
                OccurrenceId = reader.IsDBNull(8) ? null : reader.GetGuid(8), PostId = reader.IsDBNull(9) ? null : reader.GetGuid(9),
                Name = payload.Name ?? "", Status = payload.Status ?? "active", Cadence = payload.Cadence ?? "unknown", Occurrence = payload.Occurrence,
                Previous = payload.Previous, Changes = payload.Changes ?? [], RelatedEntityId = payload.RelatedEntityId,
                ReplacesOccurrenceId = payload.ReplacesOccurrenceId,
            }));
        }
        return events;
    }

    /// <summary>Reads an occurrence from the first columns of <see cref="OccurrenceColumns"/>.</summary>
    private static OccurrenceModel Occurrence(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0), Label = reader.GetString(1), Starts = reader.GetFieldValue<DateOnly>(2),
        Ends = reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3), Precision = reader.GetString(4), Certainty = reader.GetString(5),
        Predicted = reader.GetBoolean(6), Status = reader.GetString(7), Place = reader.IsDBNull(8) ? null : EntityEventLog.Place(EntityJson.Object(reader.GetString(8))),
    };

    private static string? Text(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>Represents the stored payload of a change event.</summary>
    private sealed record EventPayload
    {
        public string? Name { get; init; }
        public string? Status { get; init; }
        public string? Cadence { get; init; }
        public OccurrenceModel? Occurrence { get; init; }
        public OccurrenceModel? Previous { get; init; }
        public string[]? Changes { get; init; }
        public Guid? RelatedEntityId { get; init; }
        public Guid? ReplacesOccurrenceId { get; init; }
    }
}
