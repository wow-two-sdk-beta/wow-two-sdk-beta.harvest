using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

/// <summary>Represents one post-tags answer to store: a done answer carries its validated tags.</summary>
internal sealed record PostTagsRecord(
    Guid ContainerId,
    Guid PostId,
    byte[] ContentHash,
    int Revision,
    string Status,
    string? Error,
    int? PromptVersion,
    string? PromptHash,
    Guid? InferenceId,
    string? Model,
    ValidatedPostTags? Tags,
    DateTimeOffset? RetryAtUtc);

/// <summary>Represents what storing an answer did to the post.</summary>
internal sealed record PostTagsWriteResult(bool Stored, int? NewRevision);

internal sealed partial class ModulePostBroker
{
    /// <summary>Stores a post-tags answer and, for a done answer about the post's current title and text, resolves the
    /// post again so the tags become a new revision with <c>post.changed</c>; all in one transaction.</summary>
    /// <remarks>
    ///   - the answer is keyed by <c>(post_id, content_hash)</c>; a failed or unavailable answer never replaces a done one
    ///   - the write-back keeps the title and text, so its content hash already has a done answer and queues nothing
    ///   - identical tags leave <c>base</c> unchanged: no revision and no event
    ///   - a done answer clears the queue row for its hash; any other outcome sets the row's retry time and error
    ///   - a done answer about the current content queues the extraction its demand wants (<see cref="QueueExtractionAsync"/>)
    /// </remarks>
    internal async Task<PostTagsWriteResult> WriteTagsAsync(PostTagsRecord record, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await PostSql.LockAsync(connection, transaction, record.ContainerId, ct);
        var post = await ReadPostAsync(connection, transaction, record.ContainerId, record.PostId, ct);
        if (post is null)
        {
            await transaction.CommitAsync(ct);
            return new(false, null);
        }
        var now = Now();
        bool done = record.Status == "done" && record.Tags is not null;
        var tags = record.Tags;
        await PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_post_tags(post_id,content_hash,container_id,revision,prompt_version,prompt_hash,inference_id,model,
                entity_type,domains,topics,about,timeframe,items,language,confidence,title_en,flags,status,error,created_at)
            VALUES(@post,@hash,@container,@revision,@promptVersion,@promptHash,@inference,@model,@entity,CAST(@domains AS jsonb),@topics,
                @about,@timeframe,@items,@language,@confidence,@titleEn,CAST(@flags AS jsonb),@status,@error,@now)
            ON CONFLICT (post_id,content_hash) DO UPDATE SET revision=EXCLUDED.revision,prompt_version=EXCLUDED.prompt_version,
                prompt_hash=EXCLUDED.prompt_hash,inference_id=EXCLUDED.inference_id,model=EXCLUDED.model,entity_type=EXCLUDED.entity_type,
                domains=EXCLUDED.domains,topics=EXCLUDED.topics,about=EXCLUDED.about,timeframe=EXCLUDED.timeframe,items=EXCLUDED.items,
                language=EXCLUDED.language,confidence=EXCLUDED.confidence,title_en=EXCLUDED.title_en,flags=EXCLUDED.flags,
                status=EXCLUDED.status,error=EXCLUDED.error,created_at=EXCLUDED.created_at
            WHERE harvest_post_tags.status<>'done' OR EXCLUDED.status='done'
            """, ct, ("post", record.PostId), ("hash", record.ContentHash), ("container", record.ContainerId), ("revision", record.Revision),
            ("promptVersion", record.PromptVersion), ("promptHash", record.PromptHash), ("inference", record.InferenceId), ("model", record.Model),
            ("entity", done ? tags!.EntityType : null), ("domains", PostTagsStore.Json(done ? tags!.Domains : Array.Empty<PostTagsDomain>())),
            ("topics", done ? tags!.Topics.ToArray() : Array.Empty<string>()), ("about", tags?.About), ("timeframe", done ? tags!.Timeframe : null),
            ("items", tags?.Items), ("language", tags?.Language), ("confidence", tags?.Confidence), ("titleEn", tags?.TitleEn),
            ("flags", PostTagsStore.Json(done ? tags!.Flags : Array.Empty<PostTagsFlag>())), ("status", record.Status), ("error", record.Error), ("now", now));

        int? revised = null;
        bool current = post.MergedInto is null && PostTagsContent.Hash(post.Base).AsSpan().SequenceEqual(record.ContentHash);
        if (done && current && await ResolveAsync(connection, transaction, record.ContainerId, record.PostId, now, true, ct, onlyIfChanged: true))
        {
            var updated = (await ReadPostAsync(connection, transaction, record.ContainerId, record.PostId, ct))!;
            await DeliveryEventWriter.AppendAsync(connection, transaction, updated, "post.changed", null, null, now, postFilters, ct);
            revised = updated.Revision;
        }
        if (done && current)
            await QueueExtractionAsync(connection, transaction, record.ContainerId, record.PostId, record.ContentHash, now, ct);
        await PostSql.ExecuteAsync(connection, transaction, """
            UPDATE harvest_post_work SET lease_owner=NULL,lease_until=NULL WHERE post_id=@post AND kind=@kind;
            DELETE FROM harvest_post_work WHERE post_id=@post AND kind=@kind AND content_hash=@hash AND @done;
            UPDATE harvest_post_work SET not_before=COALESCE(CAST(@retry AS timestamptz),not_before),last_error=@error
            WHERE post_id=@post AND kind=@kind AND content_hash=@hash AND NOT @done
            """, ct, ("post", record.PostId), ("kind", PostTagsConstants.WorkKind), ("hash", record.ContentHash), ("done", done),
            ("retry", record.RetryAtUtc), ("error", record.Error));
        await transaction.CommitAsync(ct);
        return new(true, revised);
    }

    /// <summary>Reads the done answer a resolve writes back: the one for the post's current title and text, else the
    /// newest, so tags stay on the post until a new answer replaces them.</summary>
    private static async Task<StoredPostTags?> ReadStoredTagsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid postId, byte[] contentHash, CancellationToken ct)
    {
        await using var command = PostSql.Command(connection, transaction, """
            SELECT entity_type,domains::text,topics,title_en,confidence,prompt_version FROM harvest_post_tags
            WHERE post_id=@post AND status='done' ORDER BY content_hash=@hash DESC,created_at DESC LIMIT 1
            """, ("post", postId), ("hash", contentHash));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new StoredPostTags(reader.GetString(0), PostTagsStore.Read(reader.GetString(1), new List<PostTagsDomain>()),
            reader.GetFieldValue<string[]>(2), reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetDecimal(4), reader.IsDBNull(5) ? null : reader.GetInt32(5));
    }

    /// <summary>Queues post-tags for a post whose title and text have no done answer, when tagging is on and every source
    /// supplying them allows AI; a waiting row takes the newer content and starts its attempts over.</summary>
    private async Task QueueTagsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId, Guid postId,
        WoW.Two.Sdk.Beta.Be.Harvest.Extraction.PostEnvelope resolved, byte[] contentHash, DateTimeOffset now, CancellationToken ct)
    {
        if (!tagging.Enabled || !PostTagsContent.HasContent(resolved))
            return;
        await PostSql.ExecuteAsync(connection, transaction, $"""
            INSERT INTO harvest_post_work(post_id,kind,container_id,content_hash,queued_at,not_before)
            SELECT p.id,@kind,p.container_id,@hash,@now,@now FROM harvest_posts p
            WHERE p.container_id=@container AND p.id=@post AND p.merged_into IS NULL AND p.purged_at IS NULL
              AND NOT EXISTS(SELECT 1 FROM harvest_post_tags t WHERE t.post_id=p.id AND t.content_hash=@hash AND t.status='done')
              AND {PostTagsStore.AiAllowedSql}
            ON CONFLICT (post_id,kind) DO UPDATE SET content_hash=EXCLUDED.content_hash,queued_at=EXCLUDED.queued_at,
                not_before=EXCLUDED.not_before,attempts=0,last_error=NULL
            WHERE harvest_post_work.content_hash<>EXCLUDED.content_hash
            """, ct, ("kind", PostTagsConstants.WorkKind), ("hash", contentHash), ("now", now), ("container", containerId), ("post", postId));
    }
}
