using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Represents one claimed queue row.</summary>
internal sealed record EntityClaim(Guid PostId, Guid ContainerId, byte[] ContentHash, int Attempts);

/// <summary>Holds the entity layer's SQL over <c>harvest_entities</c>, <c>harvest_entity_occurrences</c>,
/// <c>harvest_entity_relations</c>, <c>harvest_entity_keep_apart</c>, <c>harvest_entity_flags</c> and the
/// <c>entity-resolve</c> kind of <c>harvest_post_work</c>.</summary>
internal static class EntityStore
{
    private const string EntityColumns = """
        e.id,e.container_id,e.entity_type,e.name,e.name_key,e.aliases,e.alias_keys,e.primary_domain,e.facts::text,e.status,e.cadence,
        e.cadence_confidence,e.first_seen_at,e.last_seen_at,e.merged_into
        """;

    private const string OccurrenceColumns =
        "o.id,o.entity_id,o.label,o.starts,o.ends,o.precision,o.certainty,o.predicted,o.status,o.place::text,o.history::text";

    /// <summary>Serializes resolution and operator changes within a container.</summary>
    internal static Task LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId, CancellationToken ct) =>
        PostSql.ScalarAsync(connection, transaction, "SELECT pg_advisory_xact_lock(hashtextextended('entities:' || CAST(@container AS text),0))",
            ct, ("container", containerId));

    /// <summary>Reads a post as resolution needs it, or null when it does not exist.</summary>
    internal static async Task<EntityPost?> ReadPostAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid postId, CancellationToken ct)
    {
        await using var command = PostSql.Command(connection, transaction, $"""
            SELECT p.container_id,p.merged_into IS NULL AND p.purged_at IS NULL,{PostTagsContent.SqlHash("p.base")},p.first_seen_at,
                p.last_changed_at,p.base->'postedAt'->>'value',l.source_id,COALESCE(s.display_name,s.handle),l.external_key,l.url,
                COALESCE(NULLIF(btrim(p.base->'author'->>'url'),''),NULLIF(btrim(p.base->'author'->>'handle'),''),
                    NULLIF(btrim(p.base->'author'->>'name'),''))
            FROM harvest_posts p
            LEFT JOIN LATERAL (SELECT l.source_id,l.external_key,l.url FROM harvest_post_links l WHERE l.post_id=p.id
                ORDER BY COALESCE(l.id::text=COALESCE(p.field_prov->>'title',p.field_prov->>'text'),false) DESC,l.retired_at IS NULL DESC,l.first_seen_at,l.id
                LIMIT 1) l ON true
            LEFT JOIN harvest_sources s ON s.id=l.source_id
            WHERE p.id=@post
            """, ("post", postId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        Guid? source = reader.IsDBNull(6) ? null : reader.GetGuid(6);
        string? author = reader.IsDBNull(10) ? null : reader.GetString(10);
        return new EntityPost(postId, reader.GetGuid(0), reader.GetFieldValue<byte[]>(2), reader.GetBoolean(1),
            reader.GetFieldValue<DateTimeOffset>(3), reader.GetFieldValue<DateTimeOffset>(4), PostedOn(reader.IsDBNull(5) ? null : reader.GetString(5)),
            source, reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9), source is { } id && author is not null ? AuthorKey(id, author) : null);
    }

    /// <summary>Hashes an author's url, handle or name on one source, so offers compare authors without keeping them.</summary>
    internal static string AuthorKey(Guid sourceId, string author) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceId:D}\u001f{author.Trim().ToLowerInvariant()}")))[..32];

    /// <summary>Reads the done extraction answers for a post's content, event first.</summary>
    internal static async Task<List<EntityAnswer>> ReadAnswersAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid postId,
        byte[] contentHash, CancellationToken ct)
    {
        var answers = new List<EntityAnswer>();
        await using var command = PostSql.Command(connection, transaction, """
            SELECT use_case,role,answer::text,updated_at FROM harvest_post_extractions
            WHERE post_id=@post AND content_hash=@hash AND status='done' ORDER BY use_case
            """, ("post", postId), ("hash", contentHash));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            answers.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)));
        return answers;
    }

    /// <summary>Reads a post's existing bindings for one use case.</summary>
    internal static async Task<List<EntityBinding>> ReadBindingsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid postId,
        string useCase, CancellationToken ct)
    {
        var bindings = new List<EntityBinding>();
        await using var command = PostSql.Command(connection, transaction, """
            SELECT entity_id,occurrence_id,content_hash FROM harvest_entity_relations WHERE post_id=@post AND use_case=@useCase
            ORDER BY created_at,entity_id
            """, ("post", postId), ("useCase", useCase));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            bindings.Add(new(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetFieldValue<byte[]>(2)));
        return bindings;
    }

    /// <summary>Reads the entities kept apart from any of <paramref name="entityIds"/>.</summary>
    internal static async Task<HashSet<Guid>> ReadKeptApartAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId,
        IReadOnlyCollection<Guid> entityIds, CancellationToken ct)
    {
        var apart = new HashSet<Guid>();
        if (entityIds.Count == 0)
            return apart;
        await using var command = PostSql.Command(connection, transaction, """
            SELECT CASE WHEN left_entity_id=ANY(@ids) THEN right_entity_id ELSE left_entity_id END FROM harvest_entity_keep_apart
            WHERE container_id=@container AND (left_entity_id=ANY(@ids) OR right_entity_id=ANY(@ids))
            """, ("container", containerId), ("ids", entityIds.ToArray()));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            apart.Add(reader.GetGuid(0));
        return apart;
    }

    /// <summary>Whether two entities are kept apart.</summary>
    internal static async Task<bool> KeptApartAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId, Guid left,
        Guid right, CancellationToken ct) =>
        await PostSql.ScalarAsync(connection, transaction, """
            SELECT 1 FROM harvest_entity_keep_apart WHERE container_id=@container AND left_entity_id=LEAST(@left,@right)
              AND right_entity_id=GREATEST(@left,@right)
            """, ct, ("container", containerId), ("left", left), ("right", right)) is not null;

    /// <summary>Reads open event entities of a container whose name or an alias is similar to one of the keys, with the
    /// best trigram similarity, at least <paramref name="floor"/>, best first.</summary>
    internal static async Task<List<(EntityRow Entity, double Score)>> ReadEventCandidatesAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid containerId, IReadOnlyList<string> keys, double floor, CancellationToken ct)
    {
        var candidates = new List<(EntityRow, double)>();
        await using var command = PostSql.Command(connection, transaction, $"""
            SELECT * FROM (
                SELECT {EntityColumns},GREATEST(
                    (SELECT max(similarity(e.name_key,k)) FROM unnest(CAST(@keys AS text[])) k),
                    COALESCE((SELECT max(similarity(a,k)) FROM unnest(e.alias_keys) a,unnest(CAST(@keys AS text[])) k),0)) AS score
                FROM harvest_entities e
                WHERE e.container_id=@container AND e.entity_type='event' AND e.merged_into IS NULL) c
            WHERE c.score>=@floor ORDER BY c.score DESC,c.last_seen_at DESC LIMIT 20
            """, ("keys", keys.ToArray()), ("container", containerId), ("floor", (float)Math.Max(0, floor - 0.2)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            candidates.Add((ReadEntity(reader), reader.GetFloat(15)));
        return candidates;
    }

    /// <summary>Reads open listing entities of a container that could offer the same thing: the same offer type and
    /// city where both are known, most recently seen first.</summary>
    internal static async Task<List<EntityRow>> ReadListingCandidatesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid containerId, string? offerType, string? cityKey, CancellationToken ct)
    {
        var candidates = new List<EntityRow>();
        await using var command = PostSql.Command(connection, transaction, $"""
            SELECT {EntityColumns} FROM harvest_entities e
            WHERE e.container_id=@container AND e.entity_type='listing' AND e.merged_into IS NULL
              AND (CAST(@offer AS text) IS NULL OR e.facts->>'offerType' IS NULL OR e.facts->>'offerType'=@offer)
              AND (CAST(@city AS text) IS NULL OR e.facts->'location'->>'cityKey' IS NULL OR e.facts->'location'->>'cityKey'=@city)
            ORDER BY e.last_seen_at DESC,e.id LIMIT 500
            """, ("container", containerId), ("offer", offerType), ("city", cityKey));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            candidates.Add(ReadEntity(reader));
        return candidates;
    }

    /// <summary>Reads the open listing an ad's source and external key already offer on, or null.</summary>
    internal static async Task<Guid?> ReadListingByLinkAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId,
        Guid? sourceId, string? externalKey, Guid postId, CancellationToken ct)
    {
        if (sourceId is null || externalKey is null)
            return null;
        return await PostSql.ScalarAsync(connection, transaction, """
            SELECT r.entity_id FROM harvest_entity_relations r JOIN harvest_entities e ON e.id=r.entity_id
            WHERE r.container_id=@container AND r.offer->>'sourceId'=@source AND r.offer->>'externalKey'=@key AND r.post_id<>@post
              AND e.merged_into IS NULL
            ORDER BY r.created_at LIMIT 1
            """, ct, ("container", containerId), ("source", sourceId.Value.ToString("D")), ("key", externalKey), ("post", postId)) as Guid?;
    }

    /// <summary>Reads which of <paramref name="entityIds"/> hold an offer from <paramref name="authorKey"/>.</summary>
    internal static async Task<HashSet<Guid>> ReadAuthoredAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyCollection<Guid> entityIds, string? authorKey, CancellationToken ct)
    {
        var authored = new HashSet<Guid>();
        if (authorKey is null || entityIds.Count == 0)
            return authored;
        await using var command = PostSql.Command(connection, transaction, """
            SELECT DISTINCT entity_id FROM harvest_entity_relations WHERE entity_id=ANY(@ids) AND offer->>'authorKey'=@author
            """, ("ids", entityIds.ToArray()), ("author", authorKey));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            authored.Add(reader.GetGuid(0));
        return authored;
    }

    /// <summary>Reads every offer of a listing with its post, earliest first.</summary>
    internal static async Task<List<(Guid PostId, JsonObject Offer)>> ReadOffersAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid entityId, CancellationToken ct)
    {
        var offers = new List<(Guid, JsonObject)>();
        await using var command = PostSql.Command(connection, transaction, """
            SELECT post_id,offer::text FROM harvest_entity_relations WHERE entity_id=@entity AND offer IS NOT NULL ORDER BY created_at,post_id
            """, ("entity", entityId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            offers.Add((reader.GetGuid(0), EntityJson.Object(reader.GetString(1))));
        return offers;
    }

    /// <summary>Reads an entity, or null.</summary>
    internal static async Task<EntityRow?> ReadEntityAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid entityId, CancellationToken ct)
    {
        await using var command = PostSql.Command(connection, transaction, $"SELECT {EntityColumns} FROM harvest_entities e WHERE e.id=@id",
            ("id", entityId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadEntity(reader) : null;
    }

    /// <summary>Reads an entity's occurrences, earliest first.</summary>
    internal static async Task<List<OccurrenceRow>> ReadOccurrencesAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid entityId,
        CancellationToken ct)
    {
        var occurrences = new List<OccurrenceRow>();
        await using var command = PostSql.Command(connection, transaction,
            $"SELECT {OccurrenceColumns} FROM harvest_entity_occurrences o WHERE o.entity_id=@entity ORDER BY o.starts,o.id", ("entity", entityId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            occurrences.Add(new OccurrenceRow
            {
                Id = reader.GetGuid(0), EntityId = reader.GetGuid(1), Label = reader.GetString(2), Starts = reader.GetFieldValue<DateOnly>(3),
                Ends = reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4), Precision = reader.GetString(5),
                Certainty = reader.GetString(6), Predicted = reader.GetBoolean(7), Status = reader.GetString(8),
                Place = reader.IsDBNull(9) ? null : EntityJson.Object(reader.GetString(9)), History = EntityJson.Array(reader.GetString(10)),
            });
        return occurrences;
    }

    /// <summary>Inserts or updates an entity row.</summary>
    internal static Task SaveEntityAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, EntityRow entity, DateTimeOffset now,
        CancellationToken ct) =>
        PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_entities(id,container_id,entity_type,name,name_key,aliases,alias_keys,primary_domain,facts,status,cadence,
                cadence_confidence,first_seen_at,last_seen_at,merged_into,created_at,updated_at)
            VALUES(@id,@container,@type,@name,@key,@aliases,@aliasKeys,@domain,CAST(@facts AS jsonb),@status,@cadence,@confidence,@first,@last,
                @merged,@now,@now)
            ON CONFLICT (id) DO UPDATE SET name=EXCLUDED.name,name_key=EXCLUDED.name_key,aliases=EXCLUDED.aliases,
                alias_keys=EXCLUDED.alias_keys,primary_domain=EXCLUDED.primary_domain,facts=EXCLUDED.facts,status=EXCLUDED.status,
                cadence=EXCLUDED.cadence,cadence_confidence=EXCLUDED.cadence_confidence,first_seen_at=EXCLUDED.first_seen_at,
                last_seen_at=EXCLUDED.last_seen_at,merged_into=EXCLUDED.merged_into,updated_at=EXCLUDED.updated_at
            """, ct, ("id", entity.Id), ("container", entity.ContainerId), ("type", entity.EntityType), ("name", entity.Name),
            ("key", entity.NameKey), ("aliases", entity.Aliases.ToArray()), ("aliasKeys", entity.AliasKeys.ToArray()),
            ("domain", entity.PrimaryDomain), ("facts", entity.Facts.ToJsonString()), ("status", entity.Status), ("cadence", entity.Cadence),
            ("confidence", entity.CadenceConfidence), ("first", entity.FirstSeenAt), ("last", entity.LastSeenAt), ("merged", entity.MergedInto),
            ("now", now));

    /// <summary>Writes the occurrences that changed: inserts new rows, updates dirty ones, deletes removed ones.</summary>
    internal static async Task SaveOccurrencesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<OccurrenceRow> occurrences,
        DateTimeOffset now, CancellationToken ct)
    {
        foreach (var occurrence in occurrences.Where(row => row.Deleted && !row.IsNew))
            await PostSql.ExecuteAsync(connection, transaction, "DELETE FROM harvest_entity_occurrences WHERE id=@id", ct, ("id", occurrence.Id));
        foreach (var occurrence in occurrences.Where(row => !row.Deleted && (row.IsNew || row.Dirty)))
            await PostSql.ExecuteAsync(connection, transaction, """
                INSERT INTO harvest_entity_occurrences(id,entity_id,label,starts,ends,precision,certainty,predicted,status,place,history,
                    created_at,updated_at)
                VALUES(@id,@entity,@label,@starts,@ends,@precision,@certainty,@predicted,@status,CAST(@place AS jsonb),CAST(@history AS jsonb),
                    @now,@now)
                ON CONFLICT (id) DO UPDATE SET entity_id=EXCLUDED.entity_id,label=EXCLUDED.label,starts=EXCLUDED.starts,ends=EXCLUDED.ends,
                    precision=EXCLUDED.precision,certainty=EXCLUDED.certainty,predicted=EXCLUDED.predicted,status=EXCLUDED.status,
                    place=EXCLUDED.place,history=EXCLUDED.history,updated_at=EXCLUDED.updated_at
                """, ct, ("id", occurrence.Id), ("entity", occurrence.EntityId), ("label", occurrence.Label), ("starts", occurrence.Starts),
                ("ends", occurrence.Ends), ("precision", occurrence.Precision), ("certainty", occurrence.Certainty),
                ("predicted", occurrence.Predicted), ("status", occurrence.Status), ("place", occurrence.Place?.ToJsonString()),
                ("history", occurrence.History.ToJsonString()), ("now", now));
    }

    /// <summary>Binds a post to an entity, or updates its binding, and drops the post's other bindings for the use case.</summary>
    internal static async Task BindAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, EntityPost post, string useCase, Guid entityId,
        Guid? occurrenceId, string role, IReadOnlyList<string> changes, JsonObject? offer, decimal confidence, DateTimeOffset now, CancellationToken ct)
    {
        await PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_entity_relations(post_id,entity_id,container_id,content_hash,use_case,occurrence_id,role,changes,offer,confidence,
                created_at,updated_at)
            VALUES(@post,@entity,@container,@hash,@useCase,@occurrence,@role,@changes,CAST(@offer AS jsonb),@confidence,@now,@now)
            ON CONFLICT (post_id,entity_id) DO UPDATE SET content_hash=EXCLUDED.content_hash,use_case=EXCLUDED.use_case,
                occurrence_id=EXCLUDED.occurrence_id,role=EXCLUDED.role,changes=EXCLUDED.changes,offer=EXCLUDED.offer,
                confidence=EXCLUDED.confidence,updated_at=EXCLUDED.updated_at;
            DELETE FROM harvest_entity_relations WHERE post_id=@post AND use_case=@useCase AND entity_id<>@entity;
            UPDATE harvest_entity_flags SET resolved_at=@now WHERE post_id=@post AND use_case=@useCase AND resolved_at IS NULL
            """, ct, ("post", post.PostId), ("entity", entityId), ("container", post.ContainerId), ("hash", post.ContentHash), ("useCase", useCase),
            ("occurrence", occurrenceId), ("role", role), ("changes", changes.ToArray()), ("offer", offer?.ToJsonString()),
            ("confidence", confidence), ("now", now));
    }

    /// <summary>Records an unsure resolution for review; the post's existing bindings stay.</summary>
    internal static Task FlagAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, EntityPost post, string useCase, string field,
        string reason, Guid? candidateId, decimal? confidence, DateTimeOffset now, CancellationToken ct) =>
        PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_entity_flags(post_id,content_hash,use_case,field,container_id,reason,candidate_id,confidence,created_at)
            VALUES(@post,@hash,@useCase,@field,@container,@reason,@candidate,@confidence,@now)
            ON CONFLICT (post_id,content_hash,use_case,field) DO UPDATE SET reason=EXCLUDED.reason,candidate_id=EXCLUDED.candidate_id,
                confidence=EXCLUDED.confidence,created_at=EXCLUDED.created_at,resolved_at=NULL
            """, ct, ("post", post.PostId), ("hash", post.ContentHash), ("useCase", useCase), ("field", field), ("container", post.ContainerId),
            ("reason", reason), ("candidate", candidateId), ("confidence", confidence), ("now", now));

    /// <summary>Queues a post's resolution for its content inside the extraction's transaction; a waiting row takes newer
    /// content and starts its attempts over.</summary>
    internal static Task<int> QueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId, Guid postId,
        byte[] contentHash, DateTimeOffset now, CancellationToken ct) =>
        PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_post_work(post_id,kind,container_id,content_hash,queued_at,not_before)
            SELECT p.id,@kind,p.container_id,@hash,@now,@now FROM harvest_posts p
            WHERE p.container_id=@container AND p.id=@post AND p.merged_into IS NULL AND p.purged_at IS NULL
            ON CONFLICT (post_id,kind) DO UPDATE SET content_hash=EXCLUDED.content_hash,queued_at=EXCLUDED.queued_at,
                not_before=EXCLUDED.not_before,attempts=0,last_error=NULL
            WHERE harvest_post_work.content_hash<>EXCLUDED.content_hash
            """, ct, ("kind", EntityConstants.WorkKind), ("hash", contentHash), ("now", now), ("container", containerId), ("post", postId));

    /// <summary>Claims up to <paramref name="batch"/> due resolutions under a lease, counting the attempt.</summary>
    internal static async Task<IReadOnlyList<EntityClaim>> ClaimAsync(NpgsqlDataSource dataSource, string owner, DateTimeOffset now, TimeSpan lease,
        int batch, CancellationToken ct)
    {
        var claims = new List<EntityClaim>();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = PostSql.Command(connection, null, """
            UPDATE harvest_post_work w SET lease_owner=@owner,lease_until=@until,attempts=w.attempts+1
            FROM (SELECT post_id FROM harvest_post_work
                  WHERE kind=@kind AND not_before<=@now AND (lease_until IS NULL OR lease_until<@now)
                  ORDER BY not_before,post_id LIMIT @batch FOR UPDATE SKIP LOCKED) due
            WHERE w.post_id=due.post_id AND w.kind=@kind
            RETURNING w.post_id,w.container_id,w.content_hash,w.attempts
            """, ("owner", owner), ("until", now + lease), ("kind", EntityConstants.WorkKind), ("now", now), ("batch", batch));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            claims.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetFieldValue<byte[]>(2), reader.GetInt32(3)));
        return claims;
    }

    /// <summary>Settles a claimed row: a finished or moot resolution deletes it for its content; a failure sets the retry
    /// time and error. A row re-queued for newer content stays, released.</summary>
    internal static async Task SettleAsync(NpgsqlDataSource dataSource, EntityClaim claim, bool done, DateTimeOffset? retryAt, string? error,
        CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await PostSql.ExecuteAsync(connection, null, """
            UPDATE harvest_post_work SET lease_owner=NULL,lease_until=NULL WHERE post_id=@post AND kind=@kind;
            DELETE FROM harvest_post_work WHERE post_id=@post AND kind=@kind AND content_hash=@hash AND @done;
            UPDATE harvest_post_work SET not_before=COALESCE(CAST(@retry AS timestamptz),not_before),last_error=@error
            WHERE post_id=@post AND kind=@kind AND content_hash=@hash AND NOT @done
            """, ct, ("post", claim.PostId), ("kind", EntityConstants.WorkKind), ("hash", claim.ContentHash), ("done", done), ("retry", retryAt),
            ("error", error));
    }

    /// <summary>Queues stored posts whose current content has a done extraction answer and neither a relation nor a flag
    /// for it; a waiting row is re-armed unless a worker holds it.</summary>
    internal static async Task<int> BackfillAsync(NpgsqlDataSource dataSource, DateTimeOffset? since, int limit, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        string hash = PostTagsContent.SqlHash("p.base");
        return await PostSql.ExecuteAsync(connection, null, $"""
            WITH due AS (
                SELECT p.id,p.container_id,{hash} AS content_hash,max(x.updated_at) AS answered FROM harvest_posts p
                JOIN harvest_post_extractions x ON x.post_id=p.id AND x.content_hash={hash} AND x.status='done'
                WHERE p.merged_into IS NULL AND p.purged_at IS NULL
                  AND (CAST(@since AS timestamptz) IS NULL OR x.updated_at>=@since)
                  AND NOT EXISTS(SELECT 1 FROM harvest_entity_relations r WHERE r.post_id=p.id AND r.content_hash=x.content_hash AND r.use_case=x.use_case)
                  AND NOT EXISTS(SELECT 1 FROM harvest_entity_flags f WHERE f.post_id=p.id AND f.content_hash=x.content_hash AND f.use_case=x.use_case)
                GROUP BY p.id,p.container_id,{hash}
                ORDER BY answered DESC,p.id LIMIT @limit)
            INSERT INTO harvest_post_work(post_id,kind,container_id,content_hash,queued_at,not_before)
            SELECT id,@kind,container_id,content_hash,@now,@now FROM due
            ON CONFLICT (post_id,kind) DO UPDATE SET content_hash=EXCLUDED.content_hash,queued_at=EXCLUDED.queued_at,
                not_before=EXCLUDED.not_before,attempts=0,last_error=NULL
            WHERE harvest_post_work.lease_owner IS NULL
            """, ct, ("kind", EntityConstants.WorkKind), ("since", since), ("limit", limit), ("now", now));
    }

    /// <summary>Reads an entity's relations with the source that published each post, earliest first.</summary>
    internal static async Task<List<EntityRelationModel>> ReadRelationsAsync(NpgsqlConnection connection, Guid entityId, CancellationToken ct)
    {
        var relations = new List<EntityRelationModel>();
        await using var command = PostSql.Command(connection, null, """
            SELECT r.post_id,r.role,r.changes,r.occurrence_id,r.confidence,r.offer::text,r.created_at,
                (SELECT COALESCE(s.display_name,s.handle) FROM harvest_post_links l JOIN harvest_sources s ON s.id=l.source_id
                 WHERE l.post_id=r.post_id ORDER BY l.retired_at IS NULL DESC,l.first_seen_at,l.id LIMIT 1)
            FROM harvest_entity_relations r WHERE r.entity_id=@entity ORDER BY r.created_at,r.post_id
            """, ("entity", entityId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            relations.Add(new EntityRelationModel
            {
                PostId = reader.GetGuid(0), Role = reader.GetString(1), Changes = reader.GetFieldValue<string[]>(2),
                OccurrenceId = reader.IsDBNull(3) ? null : reader.GetGuid(3), Confidence = reader.GetDecimal(4),
                Offer = reader.IsDBNull(5) ? null : System.Text.Json.JsonDocument.Parse(reader.GetString(5)).RootElement.Clone(),
                CreatedAtUtc = reader.GetFieldValue<DateTimeOffset>(6), Source = reader.IsDBNull(7) ? null : reader.GetString(7),
            });
        return relations;
    }

    /// <summary>Finds open entities whose name or an alias is similar to the query keys or text.</summary>
    internal static async Task<List<EntitySearchHitModel>> SearchAsync(NpgsqlDataSource dataSource, IReadOnlyList<string> keys, string text,
        string? entityType, string? domain, int limit, CancellationToken ct)
    {
        var hits = new List<EntitySearchHitModel>();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = PostSql.Command(connection, null, """
            SELECT * FROM (
                SELECT e.id,e.container_id,e.entity_type,e.name,e.primary_domain,e.cadence,e.status,GREATEST(
                    COALESCE((SELECT max(GREATEST(similarity(e.name_key,k),word_similarity(k,e.name_key))) FROM unnest(CAST(@keys AS text[])) k),0),
                    COALESCE((SELECT max(GREATEST(similarity(a,k),word_similarity(k,a))) FROM unnest(e.alias_keys) a,unnest(CAST(@keys AS text[])) k),0),
                    similarity(lower(e.name),lower(@text)),
                    COALESCE((SELECT max(similarity(lower(a),lower(@text))) FROM unnest(e.aliases) a),0)) AS score,
                    (SELECT array_agg(DISTINCT CAST(extract(year FROM o.starts) AS integer) ORDER BY CAST(extract(year FROM o.starts) AS integer))
                     FROM harvest_entity_occurrences o WHERE o.entity_id=e.id AND NOT o.predicted) AS years,
                    (SELECT min(o.starts) FROM harvest_entity_occurrences o WHERE o.entity_id=e.id AND o.predicted) AS predicted,
                    (SELECT count(*) FROM harvest_entity_relations r WHERE r.entity_id=e.id) AS posts
                FROM harvest_entities e
                WHERE e.merged_into IS NULL AND (CAST(@type AS text) IS NULL OR e.entity_type=@type)
                  AND (CAST(@domain AS text) IS NULL OR e.primary_domain=@domain OR left(e.primary_domain,length(@domain)+1)=@domain || '/')) h
            WHERE h.score>=0.35 ORDER BY h.score DESC,h.name,h.id LIMIT @limit
            """, ("keys", keys.ToArray()), ("text", text), ("type", entityType), ("domain", domain), ("limit", limit));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            hits.Add(new EntitySearchHitModel
            {
                Id = reader.GetGuid(0), ContainerId = reader.GetGuid(1), EntityType = reader.GetString(2), Name = reader.GetString(3),
                PrimaryDomain = reader.IsDBNull(4) ? null : reader.GetString(4), Cadence = reader.GetString(5), Status = reader.GetString(6),
                Score = Math.Round((decimal)reader.GetFloat(7), 3), Years = reader.IsDBNull(8) ? [] : reader.GetFieldValue<int[]>(8),
                Predicted = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9), Posts = reader.GetInt64(10),
            });
        return hits;
    }

    /// <summary>Counts entities, occurrences, relations, open flags and the queue; never post text.</summary>
    internal static async Task<EntityReportModel> CountAsync(NpgsqlDataSource dataSource, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var flags = new List<EntityFlagCount>();
        await using (var command = PostSql.Command(connection, null, """
                         SELECT field,reason,count(*) FROM harvest_entity_flags WHERE resolved_at IS NULL GROUP BY 1,2 ORDER BY 3 DESC,1,2
                         """))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                flags.Add(new EntityFlagCount { Field = reader.GetString(0), Reason = reader.GetString(1), Count = reader.GetInt64(2) });
        return new EntityReportModel
        {
            Types = await CountByAsync(connection, "SELECT entity_type,count(*) FROM harvest_entities WHERE merged_into IS NULL GROUP BY 1", ct),
            Cadences = await CountByAsync(connection,
                "SELECT cadence,count(*) FROM harvest_entities WHERE merged_into IS NULL AND entity_type='event' GROUP BY 1", ct),
            Statuses = await CountByAsync(connection, "SELECT status,count(*) FROM harvest_entities WHERE merged_into IS NULL GROUP BY 1", ct),
            Occurrences = await CountByAsync(connection,
                "SELECT CASE WHEN predicted THEN 'predicted' ELSE status END,count(*) FROM harvest_entity_occurrences GROUP BY 1", ct),
            Roles = await CountByAsync(connection, "SELECT role,count(*) FROM harvest_entity_relations GROUP BY 1", ct),
            Flags = flags,
            Merged = (long)(await PostSql.ScalarAsync(connection, null, "SELECT count(*) FROM harvest_entities WHERE merged_into IS NOT NULL", ct))!,
            KeptApart = (long)(await PostSql.ScalarAsync(connection, null, "SELECT count(*) FROM harvest_entity_keep_apart", ct))!,
            Queued = (long)(await PostSql.ScalarAsync(connection, null, "SELECT count(*) FROM harvest_post_work WHERE kind=@kind", ct,
                ("kind", EntityConstants.WorkKind)))!,
        };
    }

    private static async Task<SortedDictionary<string, long>> CountByAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        await using var command = PostSql.Command(connection, null, sql);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            counts[reader.GetString(0)] = reader.GetInt64(1);
        return counts;
    }

    private static EntityRow ReadEntity(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0), ContainerId = reader.GetGuid(1), EntityType = reader.GetString(2), Name = reader.GetString(3),
        NameKey = reader.GetString(4), Aliases = [.. reader.GetFieldValue<string[]>(5)], AliasKeys = [.. reader.GetFieldValue<string[]>(6)],
        PrimaryDomain = reader.IsDBNull(7) ? null : reader.GetString(7), Facts = EntityJson.Object(reader.GetString(8)),
        Status = reader.GetString(9), Cadence = reader.GetString(10), CadenceConfidence = reader.GetDecimal(11),
        FirstSeenAt = reader.GetFieldValue<DateTimeOffset>(12), LastSeenAt = reader.GetFieldValue<DateTimeOffset>(13),
        MergedInto = reader.IsDBNull(14) ? null : reader.GetGuid(14),
    };

    private static DateOnly? PostedOn(string? value)
    {
        if (value is not { Length: >= 10 })
            return null;
        return DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }
}
