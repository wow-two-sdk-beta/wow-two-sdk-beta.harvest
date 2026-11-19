using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Represents one claimed queue row: the post, its use case and the content it waits to process.</summary>
internal sealed record PostExtractionClaim(Guid PostId, string UseCase, Guid ContainerId, byte[] ContentHash, int Attempts);

/// <summary>Represents one extraction answer to store: a done answer carries the validated answer, role and flags.</summary>
internal sealed record PostExtractionRecord(
    Guid ContainerId,
    Guid PostId,
    byte[] ContentHash,
    string UseCase,
    int Revision,
    string Status,
    string? Error,
    int? PromptVersion,
    string? PromptHash,
    Guid? InferenceId,
    string? Model,
    ValidatedExtraction? Answer,
    int Attempts,
    DateTimeOffset? RetryAtUtc);

/// <summary>Holds the extraction's SQL over <c>harvest_post_extractions</c>, its <c>harvest_post_work</c> kinds and the
/// done <c>harvest_post_tags</c> answers it reads.</summary>
internal static class PostExtractionStore
{
    /// <summary>Reads the done tags for a post's content, or null when that content has none.</summary>
    internal static async Task<ExtractionTags?> ReadTagsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid postId,
        byte[] contentHash, CancellationToken ct)
    {
        await using var command = PostSql.Command(connection, transaction, """
            SELECT entity_type,domains::text,about,timeframe,title_en FROM harvest_post_tags
            WHERE post_id=@post AND content_hash=@hash AND status='done'
            """, ("post", postId), ("hash", contentHash));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new ExtractionTags(reader.GetString(0), PostTagsStore.Read(reader.GetString(1), new List<PostTagsDomain>()),
            reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    /// <summary>Reads the done tags for a post's content over a connection of its own.</summary>
    internal static async Task<ExtractionTags?> ReadTagsAsync(NpgsqlDataSource dataSource, Guid postId, byte[] contentHash, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        return await ReadTagsAsync(connection, null, postId, contentHash, ct);
    }

    /// <summary>Queues one use case for a post's content inside a post write's transaction, unless that content already
    /// has a done answer or a source supplying the title or text does not allow AI; a waiting row takes newer content.</summary>
    internal static Task<int> QueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId, Guid postId,
        byte[] contentHash, string useCase, DateTimeOffset now, CancellationToken ct) =>
        PostSql.ExecuteAsync(connection, transaction, $"""
            INSERT INTO harvest_post_work(post_id,kind,container_id,content_hash,queued_at,not_before)
            SELECT p.id,@kind,p.container_id,@hash,@now,@now FROM harvest_posts p
            WHERE p.container_id=@container AND p.id=@post AND p.merged_into IS NULL AND p.purged_at IS NULL
              AND NOT EXISTS(SELECT 1 FROM harvest_post_extractions e
                  WHERE e.post_id=p.id AND e.content_hash=@hash AND e.use_case=@kind AND e.status='done')
              AND {PostTagsStore.AiAllowedSql}
            ON CONFLICT (post_id,kind) DO UPDATE SET content_hash=EXCLUDED.content_hash,queued_at=EXCLUDED.queued_at,
                not_before=EXCLUDED.not_before,attempts=0,last_error=NULL
            WHERE harvest_post_work.content_hash<>EXCLUDED.content_hash
            """, ct, ("kind", useCase), ("hash", contentHash), ("now", now), ("container", containerId), ("post", postId));

    /// <summary>Claims up to <paramref name="batch"/> due extractions under a lease, counting the attempt.</summary>
    internal static async Task<IReadOnlyList<PostExtractionClaim>> ClaimAsync(NpgsqlDataSource dataSource, string owner, DateTimeOffset now,
        TimeSpan lease, int batch, CancellationToken ct)
    {
        var claims = new List<PostExtractionClaim>();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = PostSql.Command(connection, null, """
            UPDATE harvest_post_work w SET lease_owner=@owner,lease_until=@until,attempts=w.attempts+1
            FROM (SELECT post_id,kind FROM harvest_post_work
                  WHERE kind=ANY(@kinds) AND not_before<=@now AND (lease_until IS NULL OR lease_until<@now)
                  ORDER BY not_before,post_id,kind LIMIT @batch FOR UPDATE SKIP LOCKED) due
            WHERE w.post_id=due.post_id AND w.kind=due.kind
            RETURNING w.post_id,w.kind,w.container_id,w.content_hash,w.attempts
            """, ("owner", owner), ("until", now + lease), ("kinds", PostExtractionConstants.UseCases.ToArray()), ("now", now), ("batch", batch));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            claims.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetFieldValue<byte[]>(3), reader.GetInt32(4)));
        return claims;
    }

    /// <summary>Removes a claimed queue row that no longer applies: the post is gone, merged or opted out, its content
    /// moved on, or nobody wants it any more. A row re-queued for newer content stays.</summary>
    internal static async Task DropAsync(NpgsqlDataSource dataSource, PostExtractionClaim claim, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await PostSql.ExecuteAsync(connection, null, """
            DELETE FROM harvest_post_work WHERE post_id=@post AND kind=@kind AND content_hash=@hash;
            UPDATE harvest_post_work SET lease_owner=NULL,lease_until=NULL WHERE post_id=@post AND kind=@kind
            """, ct, ("post", claim.PostId), ("kind", claim.UseCase), ("hash", claim.ContentHash));
    }

    /// <summary>Stores an answer and settles its queue row in one transaction: a done answer clears the row for its
    /// content and, with <paramref name="queueEntities"/>, queues the post's entity resolution; any other outcome sets the
    /// retry time and error. A failed or unavailable answer never replaces a done one.</summary>
    internal static async Task WriteAsync(NpgsqlDataSource dataSource, PostExtractionRecord record, DateTimeOffset now, CancellationToken ct,
        bool queueEntities = false)
    {
        bool done = record.Status == "done" && record.Answer is not null;
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_post_extractions(post_id,content_hash,use_case,container_id,revision,prompt_version,prompt_hash,
                inference_id,model,role,answer,flags,status,error,attempts,created_at,updated_at)
            SELECT p.id,@hash,@useCase,p.container_id,@revision,@promptVersion,@promptHash,@inference,@model,@role,
                CAST(@answer AS jsonb),CAST(@flags AS jsonb),@status,@error,@attempts,@now,@now
            FROM harvest_posts p WHERE p.container_id=@container AND p.id=@post
            ON CONFLICT (post_id,content_hash,use_case) DO UPDATE SET revision=EXCLUDED.revision,
                prompt_version=EXCLUDED.prompt_version,prompt_hash=EXCLUDED.prompt_hash,inference_id=EXCLUDED.inference_id,
                model=EXCLUDED.model,role=EXCLUDED.role,answer=EXCLUDED.answer,flags=EXCLUDED.flags,status=EXCLUDED.status,
                error=EXCLUDED.error,attempts=EXCLUDED.attempts,updated_at=EXCLUDED.updated_at
            WHERE harvest_post_extractions.status<>'done' OR EXCLUDED.status='done'
            """, ct, ("post", record.PostId), ("hash", record.ContentHash), ("useCase", record.UseCase), ("container", record.ContainerId),
            ("revision", record.Revision), ("promptVersion", record.PromptVersion), ("promptHash", record.PromptHash),
            ("inference", record.InferenceId), ("model", record.Model), ("role", done ? record.Answer!.Role : null),
            ("answer", done ? record.Answer!.Answer.GetRawText() : null),
            ("flags", PostTagsStore.Json(done ? record.Answer!.Flags : Array.Empty<PostTagsFlag>())), ("status", record.Status),
            ("error", record.Error), ("attempts", Math.Max(1, record.Attempts)), ("now", now));
        await PostSql.ExecuteAsync(connection, transaction, """
            UPDATE harvest_post_work SET lease_owner=NULL,lease_until=NULL WHERE post_id=@post AND kind=@kind;
            DELETE FROM harvest_post_work WHERE post_id=@post AND kind=@kind AND content_hash=@hash AND @done;
            UPDATE harvest_post_work SET not_before=COALESCE(CAST(@retry AS timestamptz),not_before),last_error=@error
            WHERE post_id=@post AND kind=@kind AND content_hash=@hash AND NOT @done
            """, ct, ("post", record.PostId), ("kind", record.UseCase), ("hash", record.ContentHash), ("done", done),
            ("retry", record.RetryAtUtc), ("error", record.Error));
        if (done && queueEntities)
            await EntityStore.QueueAsync(connection, transaction, record.ContainerId, record.PostId, record.ContentHash, now, ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>Queues stored posts whose current title and text have done tags matching a wanted pair and no done answer
    /// for that use case; a waiting row is re-armed unless a worker holds it.</summary>
    internal static async Task<int> BackfillAsync(NpgsqlDataSource dataSource, IReadOnlyList<PostExtractionWant> wanted, DateTimeOffset? since,
        int limit, DateTimeOffset now, CancellationToken ct)
    {
        if (wanted.Count == 0)
            return 0;
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        string hash = PostTagsContent.SqlHash("p.base");
        return await PostSql.ExecuteAsync(connection, null, $"""
            WITH wanted AS (SELECT * FROM unnest(CAST(@types AS text[]),CAST(@domains AS text[])) AS w(entity_type,domain)),
            due AS (
                SELECT p.id,p.container_id,t.content_hash,'extract-' || t.entity_type AS kind FROM harvest_posts p
                JOIN harvest_post_tags t ON t.post_id=p.id AND t.content_hash={hash} AND t.status='done'
                WHERE p.merged_into IS NULL AND p.purged_at IS NULL
                  AND (CAST(@since AS timestamptz) IS NULL OR p.last_changed_at>=@since)
                  AND {PostTagsStore.AiAllowedSql}
                  AND EXISTS(SELECT 1 FROM wanted w,jsonb_array_elements(t.domains) d WHERE w.entity_type=t.entity_type
                      AND (d->>'node'=w.domain OR left(d->>'node',length(w.domain)+1)=w.domain || '/'))
                  AND NOT EXISTS(SELECT 1 FROM harvest_post_extractions e WHERE e.post_id=p.id AND e.content_hash=t.content_hash
                      AND e.use_case='extract-' || t.entity_type AND e.status='done')
                ORDER BY p.last_changed_at DESC,p.id LIMIT @limit)
            INSERT INTO harvest_post_work(post_id,kind,container_id,content_hash,queued_at,not_before)
            SELECT id,kind,container_id,content_hash,@now,@now FROM due
            ON CONFLICT (post_id,kind) DO UPDATE SET content_hash=EXCLUDED.content_hash,queued_at=EXCLUDED.queued_at,
                not_before=EXCLUDED.not_before,attempts=0,last_error=NULL
            WHERE harvest_post_work.lease_owner IS NULL
            """, ct, ("types", wanted.Select(want => want.EntityType).ToArray()), ("domains", wanted.Select(want => want.Domain).ToArray()),
            ("since", since), ("limit", limit), ("now", now));
    }

    /// <summary>Counts answers since a time per use case: by status and relation role, flags by field and proposed value,
    /// and the queue; never post text.</summary>
    internal static async Task<Dictionary<string, PostExtractionUseCaseReport>> CountAsync(NpgsqlDataSource dataSource, DateTimeOffset since,
        CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var statuses = await CountByAsync(connection, """
            SELECT use_case,status,count(*) FROM harvest_post_extractions WHERE updated_at>=@since GROUP BY 1,2
            """, since, ct);
        var roles = await CountByAsync(connection, """
            SELECT use_case,role,count(*) FROM harvest_post_extractions WHERE status='done' AND updated_at>=@since GROUP BY 1,2
            """, since, ct);
        var flags = PostExtractionConstants.UseCases.ToDictionary(useCase => useCase, _ => new List<PostTagsFlagCount>(), StringComparer.Ordinal);
        await using (var command = PostSql.Command(connection, null, """
                         SELECT e.use_case,f->>'field',f->>'proposed',count(*) FROM harvest_post_extractions e,jsonb_array_elements(e.flags) f
                         WHERE e.status='done' AND e.updated_at>=@since GROUP BY 1,2,3 ORDER BY 4 DESC,1,2,3 LIMIT 100
                         """, ("since", since)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                if (flags.TryGetValue(reader.GetString(0), out var list))
                    list.Add(new PostTagsFlagCount
                    {
                        Field = reader.GetString(1), Proposed = reader.IsDBNull(2) ? null : reader.GetString(2), Count = reader.GetInt64(3),
                    });
        var queued = new Dictionary<string, long>(StringComparer.Ordinal);
        await using (var command = PostSql.Command(connection, null, "SELECT kind,count(*) FROM harvest_post_work WHERE kind=ANY(@kinds) GROUP BY 1",
                         ("kinds", PostExtractionConstants.UseCases.ToArray())))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                queued[reader.GetString(0)] = reader.GetInt64(1);

        return PostExtractionConstants.UseCases.ToDictionary(useCase => useCase, useCase => new PostExtractionUseCaseReport
        {
            Statuses = statuses.GetValueOrDefault(useCase) ?? new SortedDictionary<string, long>(StringComparer.Ordinal),
            Roles = roles.GetValueOrDefault(useCase) ?? new SortedDictionary<string, long>(StringComparer.Ordinal),
            Flags = flags[useCase],
            Queued = queued.GetValueOrDefault(useCase),
        }, StringComparer.Ordinal);
    }

    private static async Task<Dictionary<string, SortedDictionary<string, long>>> CountByAsync(NpgsqlConnection connection, string sql,
        DateTimeOffset since, CancellationToken ct)
    {
        var counts = new Dictionary<string, SortedDictionary<string, long>>(StringComparer.Ordinal);
        await using var command = PostSql.Command(connection, null, sql, ("since", since));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!counts.TryGetValue(reader.GetString(0), out var byValue))
                counts[reader.GetString(0)] = byValue = new SortedDictionary<string, long>(StringComparer.Ordinal);
            byValue[reader.IsDBNull(1) ? "none" : reader.GetString(1)] = reader.GetInt64(2);
        }
        return counts;
    }
}
