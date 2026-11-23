using Microsoft.Extensions.Logging;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

internal static class EntityMatchLookup
{
    private const int ResultLimit = 3;
    private const int EvidenceLimit = 450;

    internal static async Task<EventMatchSuggestion?> AdviseAsync(NpgsqlDataSource dataSource, IEventMatchAdvisor advisor,
        ILogger logger, Guid postId, byte[]? expectedHash, CancellationToken ct)
    {
        EventMatchEvidence evidence;
        EventMatchRow[] initial;
        EntityPost post;
        EntityAnswer answer;
        await using (var connection = await dataSource.OpenConnectionAsync(ct))
        {
            if (await EntityStore.ReadPostAsync(connection, null, postId, ct) is not { Current: true } current
                || (expectedHash is not null && !expectedHash.AsSpan().SequenceEqual(current.ContentHash)))
                return null;
            post = current;
            var answers = await EntityStore.ReadAnswersAsync(connection, null, postId, post.ContentHash, ct);
            var saved = answers.FirstOrDefault(row => row.UseCase == PostExtractionConstants.EventUseCase);
            if (saved is null || PostExtractionDefinitions.Parse<ExtractEventAnswer>(saved.Json) is not { } extract)
                return null;
            answer = saved;
            var date = extract.Occurrences?.FirstOrDefault(o => o.Certainty == DateCertainty.Stated);
            string? text = await PostSql.ScalarAsync(connection, null,
                "SELECT left(base->>'text',@limit) FROM harvest_posts WHERE container_id=@container AND id=@post",
                ct, ("limit", EvidenceLimit * 2), ("container", post.ContainerId), ("post", postId)) as string;
            evidence = new(
                Clip(extract.Name?.En), Clip(extract.Name?.Original), Clip(extract.Organizer), Clip(extract.Place?.Venue),
                Clip(extract.Place?.City), EntityResolver.Wire(extract.Format), Clip(extract.EditionLabel),
                MatchableUrl(extract.Registration?.Link), answer.Role,
                date?.Starts, date?.Ends, PostTagsContent.Cut(PhoneScrub.Apply(text), EvidenceLimit));
            initial = await FindAsync(connection, post, evidence, null, ct);
        }

        if (initial.Length == 0 || (initial.Length == 1 && initial[0].NameScore >= .9
                && initial[0].Candidate.City == evidence.City && initial[0].Candidate.Format == evidence.Format))
            return null;
        logger.LogDebug("Event lookup found {Count} candidates for post {PostId}.", initial.Length, postId);

        EventMatchRow[] returned = [];
        bool used = false;
        try
        {
            var decision = await advisor.AdviseAsync(evidence, async (alternateName, cancellationToken) =>
            {
                if (used)
                    return [];
                used = true;
                if (string.IsNullOrWhiteSpace(alternateName))
                    returned = initial;
                else
                {
                    await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
                    returned = await FindAsync(connection, post, evidence, Clip(alternateName), cancellationToken);
                }
                return returned.Select(row => row.Candidate).ToArray();
            }, ct);

            if (returned.Length == 0 || decision is null)
                return null;
            if (decision is { Decision: "link", EntityId: { } id }
                && returned.FirstOrDefault(row => row.Candidate.EntityId == id) is { } match)
                return new(decision, post.ContentHash, answer.UpdatedAt, match.UpdatedAt);
            if (decision is { Decision: "review", EntityId: null })
                return new(decision, post.ContentHash, answer.UpdatedAt, null);
            if (decision is { Decision: "create", EntityId: null } && evidence.StartsOn is not null
                && evidence.Role is "announcement" or "report")
                return new(decision, post.ContentHash, answer.UpdatedAt, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Event matching advice failed for {PostId} ({ErrorType}).",
                post.PostId, exception.GetType().Name);
        }
        return null;
    }

    private static async Task<EventMatchRow[]> FindAsync(NpgsqlConnection connection, EntityPost post, EventMatchEvidence evidence,
        string? alternate, CancellationToken ct)
    {
        string[] keys = [.. new[] { evidence.Name, evidence.OriginalName, alternate }.Select(EntityNames.Key)
            .Where(key => key.Length > 0).Distinct(StringComparer.Ordinal)];
        if (keys.Length == 0)
            return [];
        DateOnly? start = EntityDates.Parse(evidence.StartsOn, Precision(evidence.StartsOn));
        DateOnly? end = EntityDates.Parse(evidence.EndsOn, Precision(evidence.EndsOn)) ?? start;
        await using var command = PostSql.Command(connection, null, """
            WITH ranked AS (
              SELECT e.*, GREATEST(
                (SELECT max(similarity(e.name_key,k)) FROM unnest(CAST(@keys AS text[])) k),
                COALESCE((SELECT max(similarity(a,k)) FROM unnest(e.alias_keys) a,unnest(CAST(@keys AS text[])) k),0)
              ) AS name_score, o.starts, o.ends, o.exact_dates, o.same_venue,
              CAST(@organizer AS text) IS NOT NULL AND lower(trim(e.facts->>'organizer'))=lower(trim(@organizer)) AS same_organizer,
              CAST(@registration AS text) IS NOT NULL AND e.facts->'registration'->>'link'=@registration AS same_registration
              FROM harvest_entities e
              LEFT JOIN LATERAL (
                SELECT o.starts,o.ends,
                  CAST(@start AS date) IS NOT NULL AND o.starts<=CAST(@end AS date)
                    AND COALESCE(o.ends,o.starts)>=CAST(@start AS date) AS exact_dates,
                  CAST(@venue AS text) IS NOT NULL
                    AND lower(trim(COALESCE(o.place->>'venue',e.facts->'place'->>'venue','')))=lower(trim(@venue)) AS same_venue
                FROM harvest_entity_occurrences o WHERE o.entity_id=e.id AND NOT o.predicted
                ORDER BY exact_dates DESC,same_venue DESC,
                  abs(o.starts-COALESCE(CAST(@start AS date),current_date)),o.id LIMIT 1
              ) o ON true
              WHERE e.container_id=@container AND e.entity_type='event' AND e.merged_into IS NULL
                AND NOT EXISTS (
                  SELECT 1 FROM harvest_entity_relations b
                  JOIN harvest_entity_keep_apart k ON k.container_id=b.container_id
                    AND k.left_entity_id=LEAST(b.entity_id,e.id) AND k.right_entity_id=GREATEST(b.entity_id,e.id)
                  WHERE b.container_id=e.container_id AND b.post_id=@post AND b.use_case='extract-event')
            )
            SELECT q.id,q.name,q.aliases,q.facts->>'organizer',q.facts->'place'->>'venue',
              q.facts->'place'->>'city',q.facts->>'format',q.starts::text,q.ends::text,
              q.updated_at,q.name_score,(
                SELECT left(p.base->>'text',@limitText) FROM harvest_entity_relations b JOIN harvest_posts p
                  ON p.container_id=b.container_id AND p.id=b.post_id
                WHERE b.container_id=q.container_id AND b.entity_id=q.id AND b.post_id<>@post
                  AND p.merged_into IS NULL AND p.purged_at IS NULL
                ORDER BY (b.role='announcement') DESC,b.created_at DESC,b.post_id LIMIT 1)
            FROM ranked q WHERE q.name_score>=.45 OR (q.exact_dates AND (q.same_venue OR q.same_organizer)) OR q.same_registration
            ORDER BY COALESCE(q.same_registration,false) DESC,(q.name_score>=.9) DESC,
                COALESCE((q.exact_dates AND (q.same_venue OR q.same_organizer)),false) DESC,
                q.name_score DESC,q.last_seen_at DESC,q.id LIMIT @limit
            """, ("container", post.ContainerId), ("post", post.PostId), ("keys", keys), ("start", start), ("end", end),
            ("venue", evidence.Venue), ("organizer", evidence.Organizer), ("registration", evidence.RegistrationUrl),
            ("limit", ResultLimit), ("limitText", EvidenceLimit * 2));
        var results = new List<EventMatchRow>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var candidate = new EventMatchCandidate(reader.GetGuid(0), Clip(reader.GetString(1))!,
                [.. reader.GetFieldValue<string[]>(2).Take(2).Select(alias => Clip(alias)!)],
                Optional(reader, 3), Optional(reader, 4), Optional(reader, 5), Optional(reader, 6),
                Optional(reader, 7), Optional(reader, 8), PostTagsContent.Cut(PhoneScrub.Apply(Optional(reader, 11)), EvidenceLimit));
            results.Add(new(candidate, reader.GetFieldValue<DateTimeOffset>(9), reader.GetFloat(10)));
        }
        return [.. results];
    }

    private static string? Optional(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static string? MatchableUrl(string? value) =>
        value is { Length: <= 200 } && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.Query.Length == 0
        && uri.AbsolutePath.Trim('/').Length >= 3 ? value : null;

    private static string? Clip(string? value) => EntityJson.Clip(value?.Trim(), 160);
    private static string Precision(string? date) => date?.Length switch { 4 => "year", 7 => "month", _ => "day" };

    private sealed record EventMatchRow(EventMatchCandidate Candidate, DateTimeOffset UpdatedAt, double NameScore);
}
