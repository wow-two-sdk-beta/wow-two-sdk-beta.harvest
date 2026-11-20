using System.Text.Json;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Represents a post as the pass reads it: the post, the strictest model-processing mode of the sources supplying
/// its title and text, and the section hint for <c>sourceCategory</c>.</summary>
internal sealed record PostTagsSubject(PostModel Post, bool Current, ModelProcessingMode Mode, string? Section)
{
    /// <summary>Gets whether work nobody named may send the post to a model: every source is <c>auto</c>.</summary>
    internal bool Automatic => Mode == ModelProcessingMode.Auto;

    /// <summary>Gets whether an operator who names the post may send it to a model: no source is <c>off</c>.</summary>
    internal bool Explicit => Mode != ModelProcessingMode.Off;

    /// <summary>Gets whether a queued row may run: an automatic one needs <c>auto</c>, an explicit one any mode but <c>off</c>.</summary>
    internal bool Allows(bool explicitRow) => explicitRow ? Explicit : Automatic;
}

/// <summary>Represents one claimed queue row; a re-run's row carries its run and whether an operator named the post.</summary>
internal sealed record PostTagsClaim(Guid PostId, Guid ContainerId, byte[] ContentHash, int Attempts, Guid? RunId = null, bool Explicit = false);

/// <summary>Holds the pass's SQL over <c>harvest_post_work</c>, <c>harvest_post_tags</c> and <c>harvest_taxonomy_nodes</c>.</summary>
internal static class PostTagsStore
{
    /// <summary>The condition, over post alias <c>p</c>, that the links supplying the title and text exist and that
    /// every one of their sources processes automatically (<c>auto</c>): what any queue nobody named needs. A source in
    /// <c>manual</c> or <c>off</c> mode keeps its words away from automatic work.</summary>
    internal const string AutoSql = """
        EXISTS(SELECT 1 FROM harvest_post_links l WHERE l.post_id=p.id AND l.id::text IN (p.field_prov->>'title',p.field_prov->>'text'))
        AND NOT EXISTS(SELECT 1 FROM harvest_post_links l JOIN harvest_sources s ON s.id=l.source_id
            WHERE l.post_id=p.id AND l.id::text IN (p.field_prov->>'title',p.field_prov->>'text') AND s.model_processing<>'auto')
        """;

    /// <summary>The strictest mode, over post alias <c>p</c>, of the sources supplying the title and text: <c>off</c> when
    /// none supplies them or any is off, <c>manual</c> when any is manual, else <c>auto</c>.</summary>
    internal const string ModeSql = """
        (SELECT CASE WHEN count(*)=0 OR bool_or(s.model_processing='off') THEN 'off'
                     WHEN bool_or(s.model_processing='manual') THEN 'manual' ELSE 'auto' END
         FROM harvest_post_links l JOIN harvest_sources s ON s.id=l.source_id
         WHERE l.post_id=p.id AND l.id::text IN (p.field_prov->>'title',p.field_prov->>'text'))
        """;

    /// <summary>Reads a mode <see cref="ModeSql"/> returns.</summary>
    internal static ModelProcessingMode ParseMode(string mode) => mode switch
    {
        "auto" => ModelProcessingMode.Auto,
        "manual" => ModelProcessingMode.Manual,
        _ => ModelProcessingMode.Off,
    };

    /// <summary>Reads the active taxonomy as <c>key — label</c> lines in key order, so the rendered system prompt stays
    /// byte-identical between calls and the provider's prompt cache holds.</summary>
    internal static async Task<(IReadOnlySet<string> Keys, string Rendered)> ReadTaxonomyAsync(NpgsqlDataSource dataSource, CancellationToken ct)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var lines = new List<string>();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = PostSql.Command(connection, null,
            "SELECT key,label FROM harvest_taxonomy_nodes WHERE active ORDER BY key COLLATE \"C\"");
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            keys.Add(reader.GetString(0));
            lines.Add($"{reader.GetString(0)} — {reader.GetString(1)}");
        }
        return (keys, string.Join('\n', lines));
    }

    /// <summary>Reads a post by id, whatever its container, or null when it does not exist.</summary>
    internal static async Task<PostTagsSubject?> ReadSubjectAsync(NpgsqlDataSource dataSource, Guid postId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        Guid containerId;
        bool current;
        ModelProcessingMode mode;
        string? section;
        await using (var command = PostSql.Command(connection, null, $"""
                         SELECT p.container_id,p.merged_into IS NULL AND p.purged_at IS NULL,{ModeSql},section.name
                         FROM harvest_posts p
                         LEFT JOIN LATERAL (
                             SELECT pl.name FROM harvest_post_links l
                             JOIN harvest_captures c ON c.container_id=l.container_id AND c.id=l.latest_capture_id AND c.captured_at=l.latest_captured_at
                             JOIN harvest_runs r ON r.id=c.run_id
                             JOIN harvest_pipelines pl ON pl.id=r.pipeline_id
                             WHERE l.post_id=p.id AND l.id::text=COALESCE(p.field_prov->>'text',p.field_prov->>'title')
                               AND COALESCE(pl.name,'')<>''
                             LIMIT 1) section ON true
                         WHERE p.id=@post
                         """, ("post", postId)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                return null;
            containerId = reader.GetGuid(0);
            current = reader.GetBoolean(1);
            mode = ParseMode(reader.GetString(2));
            section = reader.IsDBNull(3) ? null : reader.GetString(3);
        }
        var post = await ModulePostBroker.ReadPostAsync(connection, null, containerId, postId, ct);
        return post is null ? null : new PostTagsSubject(post, current, mode, section);
    }

    /// <summary>Reads the base of a post's revision from a delivered snapshot, for an eval subject that names a revision
    /// the post has moved past; null when no subscription received it.</summary>
    internal static async Task<PostEnvelope?> ReadDeliveredBaseAsync(NpgsqlDataSource dataSource, Guid postId, int revision, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var json = await PostSql.ScalarAsync(connection, null, """
            SELECT (payload->'base')::text FROM harvest_subscription_event
            WHERE post_id=@post AND revision=@revision AND payload->'base' IS NOT NULL ORDER BY id LIMIT 1
            """, ct, ("post", postId), ("revision", revision));
        return json is string text ? JsonSerializer.Deserialize<PostEnvelope>(text, JsonOptionsConstants.Default) : null;
    }

    /// <summary>Claims up to <paramref name="batch"/> due posts under a lease, counting the attempt.</summary>
    internal static async Task<IReadOnlyList<PostTagsClaim>> ClaimAsync(NpgsqlDataSource dataSource, string owner, DateTimeOffset now,
        TimeSpan lease, int batch, CancellationToken ct)
    {
        var claims = new List<PostTagsClaim>();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = PostSql.Command(connection, null, """
            UPDATE harvest_post_work w SET lease_owner=@owner,lease_until=@until,attempts=w.attempts+1
            FROM (SELECT post_id FROM harvest_post_work
                  WHERE kind=@kind AND not_before<=@now AND (lease_until IS NULL OR lease_until<@now)
                  ORDER BY not_before,post_id LIMIT @batch FOR UPDATE SKIP LOCKED) due
            WHERE w.post_id=due.post_id AND w.kind=@kind
            RETURNING w.post_id,w.container_id,w.content_hash,w.attempts,w.run_id,w.explicit
            """, ("owner", owner), ("until", now + lease), ("kind", PostTagsConstants.WorkKind), ("now", now), ("batch", batch));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            claims.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetFieldValue<byte[]>(2), reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetBoolean(5)));
        return claims;
    }

    /// <summary>Removes a post's queued tagging: the post is gone, merged, empty or its source's mode refuses the row.</summary>
    internal static async Task DropAsync(NpgsqlDataSource dataSource, Guid postId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await PostSql.ExecuteAsync(connection, null, "DELETE FROM harvest_post_work WHERE post_id=@post AND kind=@kind", ct,
            ("post", postId), ("kind", PostTagsConstants.WorkKind));
    }

    /// <summary>Queues stored posts that have no done answer for their current title and text.</summary>
    internal static async Task<int> BackfillAsync(NpgsqlDataSource dataSource, DateTimeOffset? since, int limit, Guid? sourceId,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        string hash = PostTagsContent.SqlHash("p.base");
        return await PostSql.ExecuteAsync(connection, null, $"""
            INSERT INTO harvest_post_work(post_id,kind,container_id,content_hash,queued_at,not_before)
            SELECT p.id,@kind,p.container_id,{hash},@now,@now FROM harvest_posts p
            WHERE p.merged_into IS NULL AND p.purged_at IS NULL
              AND (CAST(@since AS timestamptz) IS NULL OR p.last_changed_at>=@since)
              AND (btrim(COALESCE(p.base->>'title',''))<>'' OR btrim(COALESCE(p.base->>'text',''))<>'')
              AND (CAST(@source AS uuid) IS NULL OR EXISTS(SELECT 1 FROM harvest_post_links l
                   WHERE l.post_id=p.id AND l.source_id=@source AND l.retired_at IS NULL))
              AND {AutoSql}
              AND NOT EXISTS(SELECT 1 FROM harvest_post_tags t WHERE t.post_id=p.id AND t.content_hash={hash} AND t.status='done')
            ORDER BY p.last_changed_at DESC,p.id LIMIT @limit
            ON CONFLICT (post_id,kind) DO UPDATE SET content_hash=EXCLUDED.content_hash,queued_at=EXCLUDED.queued_at,
                not_before=EXCLUDED.not_before,attempts=0,last_error=NULL
            WHERE harvest_post_work.lease_owner IS NULL
            """, ct, ("kind", PostTagsConstants.WorkKind), ("now", now), ("since", since), ("source", sourceId), ("limit", limit));
    }

    /// <summary>Counts answers since a time: by status, entity type and primary domain, and flags by field and proposed
    /// value; never post text.</summary>
    internal static async Task<PostTagsReportModel> CountAsync(NpgsqlDataSource dataSource, DateTimeOffset since, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var statuses = await CountByAsync(connection, "SELECT status,count(*) FROM harvest_post_tags WHERE created_at>=@since GROUP BY 1", since, ct);
        var types = await CountByAsync(connection,
            "SELECT entity_type,count(*) FROM harvest_post_tags WHERE status='done' AND created_at>=@since GROUP BY 1", since, ct);
        var domains = await CountByAsync(connection, """
            SELECT split_part(d->>'node','/',1),count(*) FROM harvest_post_tags t,jsonb_array_elements(t.domains) d
            WHERE t.status='done' AND t.created_at>=@since AND (d->>'primary')::boolean GROUP BY 1
            """, since, ct);
        var flags = new List<PostTagsFlagCount>();
        await using (var command = PostSql.Command(connection, null, """
                         SELECT f->>'field',f->>'proposed',count(*) FROM harvest_post_tags t,jsonb_array_elements(t.flags) f
                         WHERE t.status='done' AND t.created_at>=@since GROUP BY 1,2 ORDER BY 3 DESC,1,2 LIMIT 50
                         """, ("since", since)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                flags.Add(new PostTagsFlagCount
                {
                    Field = reader.GetString(0), Proposed = reader.IsDBNull(1) ? null : reader.GetString(1), Count = reader.GetInt64(2),
                });
        var queued = (long)(await PostSql.ScalarAsync(connection, null, "SELECT count(*) FROM harvest_post_work WHERE kind=@kind", ct,
            ("kind", PostTagsConstants.WorkKind)))!;
        return new PostTagsReportModel
        {
            SinceUtc = since, Statuses = statuses, EntityTypes = types, Domains = domains, Flags = flags, Queued = queued,
        };
    }

    /// <summary>Serializes a validated answer's domains or flags for a jsonb column.</summary>
    internal static string Json<T>(T value) => StoreJson.Write(value);

    /// <summary>Reads a jsonb column written by <see cref="Json{T}"/>.</summary>
    internal static T Read<T>(string json, T fallback) where T : class => StoreJson.Read(json, fallback);

    private static async Task<IReadOnlyDictionary<string, long>> CountByAsync(NpgsqlConnection connection, string sql, DateTimeOffset since, CancellationToken ct)
    {
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        await using var command = PostSql.Command(connection, null, sql, ("since", since));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            counts[reader.IsDBNull(0) ? "none" : reader.GetString(0)] = reader.GetInt64(1);
        return counts;
    }

    /// <summary>Gets an element as compact JSON for printing.</summary>
    internal static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, PostTagsDefinition.Wire);
}
