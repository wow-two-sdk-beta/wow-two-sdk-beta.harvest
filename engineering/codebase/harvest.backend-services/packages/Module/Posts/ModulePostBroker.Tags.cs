using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
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
    DateTimeOffset? RetryAtUtc,
    Guid? RunId = null,
    bool Explicit = false);

/// <summary>Represents what storing an answer did to the post.</summary>
internal sealed record PostTagsWriteResult(bool Stored, int? NewRevision);

internal sealed partial class ModulePostBroker
{
    /// <summary>Stores a post-tags answer and, for a done answer about the post's current title and text, resolves the
    /// post again so the tags become a new revision with <c>post.changed</c>; all in one transaction.</summary>
    /// <remarks>
    ///   - the answer is keyed by <c>(post_id, content_hash)</c>; a failed or unavailable answer never replaces a done one
    ///   - the write-back keeps the title and text, so its content hash already has a done answer and queues nothing
    ///   - the same resolve writes the classification the tags give (<see cref="PostTagsClassification"/>): one revision
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
        string? before = done && record.RunId is not null
            ? await ExtractionInputsAsync(connection, transaction, record.PostId, record.ContentHash, ct) : null;
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
        if (done && current && record.RunId is { } runId)
            await QueueRerunExtractionAsync(connection, transaction, record, runId,
                before != await ExtractionInputsAsync(connection, transaction, record.PostId, record.ContentHash, ct), now, ct);
        else if (done && current)
            await QueueExtractionAsync(connection, transaction, record.ContainerId, record.PostId, record.ContentHash, now, ct, record.Explicit);
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

    /// <summary>Reads the done answers a resolve writes back: the tags for the post's current title and text, else the
    /// newest, so tags stay on the post until a new answer replaces them; and the done <c>extract-event</c> answer about
    /// the same content as those tags. Only an identified, dated event about the current content can supply a missing
    /// post kind.</summary>
    private static async Task<(StoredPostTags? Tags, StoredEventExtraction? Event)> ReadStoredAnswersAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid postId, byte[] contentHash, CancellationToken ct)
    {
        await using var command = PostSql.Command(connection, transaction, """
            SELECT t.entity_type,t.domains::text,t.topics,t.title_en,t.confidence,t.prompt_version,t.model,
                e.post_id IS NOT NULL,e.answer->>'format',(e.answer->'occurrences'->0)::text,t.timeframe,t.prompt_hash,
                COALESCE(e.content_hash=@hash AND e.role IN ('announcement','update','report')
                    AND (NULLIF(btrim(e.answer->'name'->>'en'),'') IS NOT NULL
                        OR NULLIF(btrim(e.answer->'name'->>'original'),'') IS NOT NULL),false)
            FROM harvest_post_tags t
            LEFT JOIN harvest_post_extractions e ON e.post_id=t.post_id AND e.content_hash=t.content_hash
                AND e.use_case=@useCase AND e.status='done' AND t.entity_type='event'
            WHERE t.post_id=@post AND t.status='done' ORDER BY t.content_hash=@hash DESC,t.created_at DESC LIMIT 1
            """, ("post", postId), ("hash", contentHash), ("useCase", PostExtractionConstants.EventUseCase));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return (null, null);
        var tags = new StoredPostTags(reader.GetString(0), PostTagsStore.Read(reader.GetString(1), new List<PostTagsDomain>()),
            reader.GetFieldValue<string[]>(2), reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetDecimal(4), reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) || !reader.GetString(11).StartsWith(PostTagsConstants.RuleHashPrefix, StringComparison.Ordinal)
                ? null : reader.GetString(11)[PostTagsConstants.RuleHashPrefix.Length..]);
        return (tags, StoredEventExtraction.Read(reader.GetBoolean(7), reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetBoolean(12)));
    }

    /// <summary>Reads what extraction takes from a done tags answer — entity type, domains, about, timeframe and English
    /// title — as one comparable text, or null when the content has no done answer.</summary>
    private static async Task<string?> ExtractionInputsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid postId,
        byte[] contentHash, CancellationToken ct) =>
        await PostSql.ScalarAsync(connection, transaction, """
            SELECT jsonb_build_array(entity_type,domains,about,timeframe,title_en)::text FROM harvest_post_tags
            WHERE post_id=@post AND content_hash=@hash AND status='done'
            """, ct, ("post", postId), ("hash", contentHash)) as string;

    /// <summary>Writes the tags the post's source sections imply, in code, when its current title and text have no done answer:
    /// a done <c>harvest_post_tags</c> row with no model, which the resolve then overlays as tier <c>Rules</c> tags in the same
    /// write, so the post raises no extra revision and no event, and queues no tagging.</summary>
    /// <remarks>
    ///   - applies only when tagging is on, and only when every link supplying the title and text sits in a section that
    ///     declares tags (<see cref="ImpliedTagResolver"/>); any other post is left to the model
    ///   - a done answer for the content, a model's included, is never replaced; a failed or unavailable one is
    ///   - the row's <c>prompt_hash</c> reads <c>rule:section-implied@1</c>, which tells it from a model answer
    ///   - the row records the revision the write gives the post (<c>bump</c> more than its stored one); the result is the
    ///     stored tags to overlay, or null when the post is not section-implied
    /// </remarks>
    private async Task<StoredPostTags?> WriteImpliedTagsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId,
        Guid postId, WoW.Two.Sdk.Beta.Be.Harvest.Extraction.PostEnvelope resolved, IReadOnlyDictionary<string, Guid> provenance, byte[] contentHash,
        int bump, DateTimeOffset now, CancellationToken ct)
    {
        if (!tagging.Enabled || !PostTagsContent.HasContent(resolved)
            || await PostSql.ScalarAsync(connection, transaction,
                "SELECT 1 FROM harvest_post_tags WHERE post_id=@post AND content_hash=@hash AND status='done'", ct,
                ("post", postId), ("hash", contentHash)) is not null)
            return null;
        var links = new[] { "title", "text" }.Where(provenance.ContainsKey).Select(field => provenance[field]).Distinct().ToArray();
        if (await implied.ResolveAsync(connection, transaction, containerId, links, ct) is not { } tags)
            return null;
        await PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_post_tags(post_id,content_hash,container_id,revision,prompt_hash,entity_type,domains,topics,timeframe,flags,status,created_at)
            SELECT p.id,@hash,p.container_id,p.revision+@bump,@rule,@entity,CAST(@domains AS jsonb),ARRAY[]::text[],@timeframe,'[]'::jsonb,'done',@now
            FROM harvest_posts p WHERE p.container_id=@container AND p.id=@post
            ON CONFLICT (post_id,content_hash) DO UPDATE SET revision=EXCLUDED.revision,prompt_version=NULL,prompt_hash=EXCLUDED.prompt_hash,
                inference_id=NULL,model=NULL,entity_type=EXCLUDED.entity_type,domains=EXCLUDED.domains,topics=EXCLUDED.topics,about=NULL,
                timeframe=EXCLUDED.timeframe,items=NULL,language=NULL,confidence=NULL,title_en=NULL,flags=EXCLUDED.flags,status='done',
                error=NULL,created_at=EXCLUDED.created_at
            WHERE harvest_post_tags.status<>'done'
            """, ct, ("post", postId), ("hash", contentHash), ("container", containerId), ("bump", bump),
            ("rule", PostTagsConstants.RuleHashPrefix + PostTagsConstants.SectionRule), ("entity", tags.EntityType),
            ("domains", PostTagsStore.Json(tags.ToStored().Domains)), ("timeframe", tags.ToStored().Timeframe), ("now", now));
        return tags.ToStored();
    }

    /// <summary>Gives a post whose source sections imply tags those tags now instead of a tagging call, for a worker that
    /// claimed its queued <c>post-tags</c> row (<c>post-tags backfill</c>, or a re-run no operator named a post or source for):
    /// the post resolves again, and a changed <c>base</c> raises its revision with <c>post.changed</c>, as any tag write does.</summary>
    /// <returns>Whether the post's current title and text now have tags a rule wrote, so no model need tag them.</returns>
    internal async Task<bool> ApplyImpliedTagsAsync(Guid containerId, Guid postId, CancellationToken ct)
    {
        if (!tagging.Enabled)
            return false;
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await PostSql.LockAsync(connection, transaction, containerId, ct);
        var post = await ReadPostAsync(connection, transaction, containerId, postId, ct);
        var links = post is null ? [] : new[] { "title", "text" }.Where(post.FieldSources.ContainsKey).Select(field => post.FieldSources[field]).Distinct().ToArray();
        if (post is not { MergedInto: null } || !PostTagsContent.HasContent(post.Base)
            || await implied.ResolveAsync(connection, transaction, containerId, links, ct) is null)
        {
            await transaction.CommitAsync(ct);
            return false;
        }
        var now = Now();
        if (await ResolveAsync(connection, transaction, containerId, postId, now, true, ct, onlyIfChanged: true))
            await DeliveryEventWriter.AppendAsync(connection, transaction,
                (await ReadPostAsync(connection, transaction, containerId, postId, ct))!, "post.changed", null, null, now, postFilters, ct);
        var current = (await ReadPostAsync(connection, transaction, containerId, postId, ct))!;
        bool ruleTagged = await PostSql.ScalarAsync(connection, transaction, """
            SELECT 1 FROM harvest_post_tags WHERE post_id=@post AND content_hash=@hash AND status='done' AND left(prompt_hash,5)=@rule
            """, ct, ("post", postId), ("hash", PostTagsContent.Hash(current.Base)), ("rule", PostTagsConstants.RuleHashPrefix)) is not null;
        await transaction.CommitAsync(ct);
        return ruleTagged;
    }

    /// <summary>Queues post-tags for a post whose title and text have no done answer, when tagging is on and every source
    /// supplying them processes automatically; a waiting row takes the newer content and starts its attempts over.</summary>
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
              AND {PostTagsStore.AutoSql}
            ON CONFLICT (post_id,kind) DO UPDATE SET content_hash=EXCLUDED.content_hash,queued_at=EXCLUDED.queued_at,
                not_before=EXCLUDED.not_before,attempts=0,last_error=NULL
            WHERE harvest_post_work.content_hash<>EXCLUDED.content_hash
            """, ct, ("kind", PostTagsConstants.WorkKind), ("hash", contentHash), ("now", now), ("container", containerId), ("post", postId));
    }
}
