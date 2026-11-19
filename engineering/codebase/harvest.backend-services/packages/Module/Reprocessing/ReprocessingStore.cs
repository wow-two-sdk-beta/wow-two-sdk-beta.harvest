using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Reprocessing;

/// <summary>Represents which posts a re-run or a sample reads: named posts, listed sources or every source, changed since a
/// time. Named posts and listed sources are explicit operator choices: a <c>manual</c> source's posts run; only a run over
/// every source leaves them out.</summary>
internal sealed record RerunScope(IReadOnlyList<Guid>? PostIds, IReadOnlyList<Guid>? SourceIds, DateTimeOffset? SinceUtc, int? PromptBefore)
{
    /// <summary>Gets whether an operator named the posts: extraction then takes any post tagged with its entity type.</summary>
    internal bool Named => PostIds is { Count: > 0 };

    /// <summary>Gets whether an operator named the posts or listed the sources: a <c>manual</c> source's posts run.</summary>
    internal bool Explicit => Named || SourceIds is { Count: > 0 };

    /// <summary>Gets the scope's name: <c>posts</c>, <c>sources</c> or <c>all</c>.</summary>
    internal string Name => Named ? PostReprocessingConstants.PostsScope
        : SourceIds is { Count: > 0 } ? PostReprocessingConstants.SourcesScope : PostReprocessingConstants.AllScope;
}

/// <summary>Represents one post a sample drew: its id, revision and, when asked for, its current done answer.</summary>
internal sealed record SampledPost(Guid PostId, int Revision, string? Current);

/// <summary>Holds the SQL of operator re-runs over <c>harvest_processing_runs</c> and the run's rows in
/// <c>harvest_post_work</c>, and of eval-set samples; never post text beyond a stored answer.</summary>
internal static class ReprocessingStore
{
    /// <summary>Reads the use cases a run asked for and whether it named posts; none for an unknown run.</summary>
    internal static async Task<(IReadOnlyList<string> UseCases, bool Named)> AskedAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid runId, CancellationToken ct)
    {
        await using var command = PostSql.Command(connection, transaction, "SELECT use_cases,scope FROM harvest_processing_runs WHERE id=@run",
            ("run", runId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.GetFieldValue<string[]>(0), reader.GetString(1) == PostReprocessingConstants.PostsScope) : ([], false);
    }

    /// <summary>Records a run before its rows queue.</summary>
    internal static Task InsertRunAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid runId, IReadOnlyList<string> useCases,
        RerunScope scope, string? requestedBy, DateTimeOffset now, CancellationToken ct) =>
        PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_processing_runs(id,use_cases,scope,source_ids,post_ids,since,prompt_before,requested_by,created_at)
            VALUES(@run,@useCases,@scope,@sources,@posts,@since,@before,@by,@now)
            """, ct, ("run", runId), ("useCases", useCases.ToArray()), ("scope", scope.Name), ("sources", (scope.SourceIds ?? []).ToArray()),
            ("posts", (scope.PostIds ?? []).ToArray()), ("since", scope.SinceUtc), ("before", scope.PromptBefore), ("by", requestedBy), ("now", now));

    /// <summary>Stores the counts a run queued with.</summary>
    internal static Task SaveCountsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid runId,
        IReadOnlyDictionary<string, PostRerunCounts> counts, CancellationToken ct) =>
        PostSql.ExecuteAsync(connection, transaction, "UPDATE harvest_processing_runs SET counts=CAST(@counts AS jsonb) WHERE id=@run", ct,
            ("run", runId), ("counts", PostTagsStore.Json(counts)));

    /// <summary>Queues one use case's posts in a run inside its transaction and counts them.</summary>
    /// <remarks>
    ///   - the posts: current, with a title or text, inside the scope and the since filter; for extraction, with done tags of
    ///     the use case's entity type and, outside named posts, a pair the demand wants; with a prompt filter, only posts
    ///     whose done answer for the current content came from an older version; an extraction leaves out the posts this
    ///     run queued for tags, as theirs follows the tags
    ///   - a post runs when every source supplying its title and text is <c>auto</c>, or none is <c>off</c> for named posts
    ///     and listed sources; a row a worker holds stays as it is
    ///   - a queued row replaces a waiting one for the post and kind, takes the run and starts its attempts over
    /// </remarks>
    internal static async Task<PostRerunCounts> QueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid runId,
        RerunScope scope, string useCase, IReadOnlyList<PostExtractionWant> wanted, DateTimeOffset now, CancellationToken ct)
    {
        string hash = PostTagsContent.SqlHash("p.base");
        bool tagging = useCase == PostTagsConstants.UseCase;
        var parameters = new List<(string, object?)>
        {
            ("run", runId), ("kind", useCase), ("now", now), ("explicit", scope.Explicit), ("since", scope.SinceUtc),
            ("posts", scope.PostIds is { Count: > 0 } posts ? posts.ToArray() : null), ("sources", scope.SourceIds is { Count: > 0 } sources ? sources.ToArray() : null),
            ("before", scope.PromptBefore),
        };
        string join = "", demand = "", follows = "", before;
        if (tagging)
        {
            before = $"""
                AND (CAST(@before AS integer) IS NULL OR EXISTS(SELECT 1 FROM harvest_post_tags x
                    WHERE x.post_id=p.id AND x.content_hash={hash} AND x.status='done' AND x.prompt_version<CAST(@before AS integer)))
                """;
        }
        else
        {
            parameters.Add(("entityType", useCase == PostExtractionConstants.EventUseCase
                ? PostExtractionConstants.EventType : PostExtractionConstants.ListingType));
            join = $"JOIN harvest_post_tags t ON t.post_id=p.id AND t.content_hash={hash} AND t.status='done' AND t.entity_type=@entityType";
            before = $"""
                AND (CAST(@before AS integer) IS NULL OR EXISTS(SELECT 1 FROM harvest_post_extractions e WHERE e.post_id=p.id
                    AND e.content_hash={hash} AND e.use_case=@kind AND e.status='done' AND e.prompt_version<CAST(@before AS integer)))
                """;
            follows = """
                AND NOT EXISTS(SELECT 1 FROM harvest_post_work w WHERE w.post_id=p.id AND w.kind='post-tags' AND w.run_id=@run)
                """;
            if (!scope.Named)
            {
                parameters.Add(("types", wanted.Select(want => want.EntityType).ToArray()));
                parameters.Add(("domains", wanted.Select(want => want.Domain).ToArray()));
                demand = """
                    AND EXISTS(SELECT 1 FROM unnest(CAST(@types AS text[]),CAST(@domains AS text[])) AS w(entity_type,domain),
                        jsonb_array_elements(t.domains) d WHERE w.entity_type=t.entity_type
                        AND (d->>'node'=w.domain OR left(d->>'node',length(w.domain)+1)=w.domain || '/'))
                    """;
            }
        }

        await using var command = PostSql.Command(connection, transaction, $"""
            WITH scope AS (
                SELECT p.id,p.container_id,{hash} AS hash,{PostTagsStore.ModeSql} AS mode FROM harvest_posts p {join}
                WHERE p.merged_into IS NULL AND p.purged_at IS NULL
                  AND (btrim(COALESCE(p.base->>'title',''))<>'' OR btrim(COALESCE(p.base->>'text',''))<>'')
                  AND (CAST(@posts AS uuid[]) IS NULL OR p.id=ANY(CAST(@posts AS uuid[])))
                  AND (CAST(@sources AS uuid[]) IS NULL OR EXISTS(SELECT 1 FROM harvest_post_links l
                       WHERE l.post_id=p.id AND l.source_id=ANY(CAST(@sources AS uuid[])) AND l.retired_at IS NULL))
                  AND (CAST(@since AS timestamptz) IS NULL OR p.last_changed_at>=@since)
                  {demand} {before} {follows}),
            eligible AS (SELECT * FROM scope WHERE mode='auto' OR mode='manual' AND @explicit),
            queued AS (
                INSERT INTO harvest_post_work(post_id,kind,container_id,content_hash,queued_at,not_before,run_id,explicit)
                SELECT id,@kind,container_id,hash,@now,@now,@run,@explicit FROM eligible
                ON CONFLICT (post_id,kind) DO UPDATE SET content_hash=EXCLUDED.content_hash,queued_at=EXCLUDED.queued_at,
                    not_before=EXCLUDED.not_before,attempts=0,last_error=NULL,run_id=EXCLUDED.run_id,explicit=EXCLUDED.explicit
                WHERE harvest_post_work.lease_owner IS NULL OR harvest_post_work.lease_until<@now
                RETURNING post_id)
            SELECT (SELECT count(*) FROM queued),(SELECT count(*) FROM eligible),
                (SELECT count(*) FROM scope WHERE mode='manual' AND NOT @explicit),(SELECT count(*) FROM scope WHERE mode='off'),
                (SELECT count(*) FROM scope)
            """, [.. parameters]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        long queued = reader.GetInt64(0), eligible = reader.GetInt64(1), manual = reader.GetInt64(2), refused = reader.GetInt64(3),
            found = reader.GetInt64(4);
        long notEligible = scope.Named ? Math.Max(0, scope.PostIds!.Count - found) : 0;
        return new PostRerunCounts
        {
            Queued = queued, InFlight = eligible - queued, Manual = manual, NotEligible = notEligible,
            Skipped = eligible - queued + manual + notEligible, Refused = refused,
        };
    }

    /// <summary>Reads a run's record: scope, filters, who asked, when, and the counts it queued with; null when unknown.</summary>
    internal static async Task<(string Scope, string[] UseCases, DateTimeOffset? Since, int? PromptBefore, string? By, DateTimeOffset At, string Counts)?>
        ReadRunAsync(NpgsqlDataSource dataSource, Guid runId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = PostSql.Command(connection, null, """
            SELECT scope,use_cases,since,prompt_before,requested_by,created_at,counts::text FROM harvest_processing_runs WHERE id=@run
            """, ("run", runId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return (reader.GetString(0), reader.GetFieldValue<string[]>(1), reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetString(6));
    }

    /// <summary>Counts a run's rows still in the queue by kind: waiting, held by a worker, and retrying after a failure with
    /// their last errors.</summary>
    internal static async Task<IReadOnlyDictionary<string, PostRerunProgress>> ProgressAsync(NpgsqlDataSource dataSource, Guid runId,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var errors = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        await using (var command = PostSql.Command(connection, null, """
                         SELECT kind,last_error,count(*) FROM harvest_post_work
                         WHERE run_id=@run AND last_error IS NOT NULL AND (lease_owner IS NULL OR lease_until<@now)
                         GROUP BY 1,2 ORDER BY 3 DESC,2 LIMIT 50
                         """, ("run", runId), ("now", now)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
            {
                if (!errors.TryGetValue(reader.GetString(0), out var byError))
                    errors[reader.GetString(0)] = byError = new Dictionary<string, long>(StringComparer.Ordinal);
                byError[reader.GetString(1)] = reader.GetInt64(2);
            }

        var progress = new SortedDictionary<string, PostRerunProgress>(StringComparer.Ordinal);
        await using (var command = PostSql.Command(connection, null, """
                         SELECT kind,
                             count(*) FILTER (WHERE (lease_owner IS NULL OR lease_until<@now) AND last_error IS NULL),
                             count(*) FILTER (WHERE lease_owner IS NOT NULL AND lease_until>=@now),
                             count(*) FILTER (WHERE (lease_owner IS NULL OR lease_until<@now) AND last_error IS NOT NULL)
                         FROM harvest_post_work WHERE run_id=@run GROUP BY kind
                         """, ("run", runId), ("now", now)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                progress[reader.GetString(0)] = new PostRerunProgress
                {
                    Waiting = reader.GetInt64(1), Running = reader.GetInt64(2), Retrying = reader.GetInt64(3),
                    Errors = errors.GetValueOrDefault(reader.GetString(0)) ?? new Dictionary<string, long>(),
                };
        return progress;
    }

    /// <summary>Draws a seeded sample of current posts for an eval set: inside the sources and since filter, no source
    /// <c>off</c>, for extraction tagged with the use case's entity type, and with a done answer when it is to be the
    /// expected one. The same seed over the same posts draws the same sample.</summary>
    internal static async Task<IReadOnlyList<SampledPost>> SampleAsync(NpgsqlDataSource dataSource, string useCase, IReadOnlyList<Guid> sourceIds,
        DateTimeOffset? since, int sample, int seed, bool current, CancellationToken ct)
    {
        string hash = PostTagsContent.SqlHash("p.base");
        bool tagging = useCase == PostTagsConstants.UseCase;
        var parameters = new List<(string, object?)>
        {
            ("sources", sourceIds.Count > 0 ? sourceIds.ToArray() : null), ("since", since), ("sample", sample), ("seed", seed.ToString("D")),
        };
        string join, expected;
        if (tagging)
        {
            join = $"LEFT JOIN harvest_post_tags c ON c.post_id=p.id AND c.content_hash={hash} AND c.status='done'";
            expected = """
                CASE WHEN c.post_id IS NULL THEN NULL ELSE jsonb_build_object('entityType',c.entity_type,'domains',c.domains,
                    'topics',to_jsonb(c.topics),'about',c.about,'timeframe',c.timeframe,'items',c.items,'language',c.language,
                    'confidence',c.confidence,'titleEn',c.title_en,'flags',c.flags)::text END
                """;
        }
        else
        {
            parameters.Add(("kind", useCase));
            parameters.Add(("entityType", useCase == PostExtractionConstants.EventUseCase
                ? PostExtractionConstants.EventType : PostExtractionConstants.ListingType));
            join = $"""
                JOIN harvest_post_tags t ON t.post_id=p.id AND t.content_hash={hash} AND t.status='done' AND t.entity_type=@entityType
                LEFT JOIN harvest_post_extractions c ON c.post_id=p.id AND c.content_hash={hash} AND c.use_case=@kind AND c.status='done'
                """;
            expected = "c.answer::text";
        }

        var posts = new List<SampledPost>();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = PostSql.Command(connection, null, $"""
            SELECT p.id,p.revision,{expected} FROM harvest_posts p {join}
            WHERE p.merged_into IS NULL AND p.purged_at IS NULL
              AND (btrim(COALESCE(p.base->>'title',''))<>'' OR btrim(COALESCE(p.base->>'text',''))<>'')
              AND {PostTagsStore.ModeSql}<>'off'
              AND (CAST(@sources AS uuid[]) IS NULL OR EXISTS(SELECT 1 FROM harvest_post_links l
                   WHERE l.post_id=p.id AND l.source_id=ANY(CAST(@sources AS uuid[])) AND l.retired_at IS NULL))
              AND (CAST(@since AS timestamptz) IS NULL OR p.last_changed_at>=@since)
              {(current ? "AND c.post_id IS NOT NULL" : "")}
            ORDER BY md5(@seed || p.id::text),p.id LIMIT @sample
            """, [.. parameters]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            posts.Add(new SampledPost(reader.GetGuid(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return posts;
    }
}
