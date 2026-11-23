using System.Text.RegularExpressions;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

internal sealed partial class ModulePostBroker
{
    private const int MinimumDuplicateText = 64;
    private const int CandidateLimit = 16;
    private const int MaximumSimilarText = 24_000;
    private const float SimilarityFloor = .89f;
    private static readonly Regex DuplicateNumbers = new(@"\p{Nd}+", RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    internal static PostDuplicateModel? ReadDuplicate(NpgsqlDataReader reader, int start) => reader.IsDBNull(start) ? null
        : new(reader.GetGuid(start), reader.GetGuid(start + 1), reader.GetString(start + 2),
            reader.GetString(start + 3), reader.GetFloat(start + 4));

    /// <summary>Rechecks a changed source reading and copies that depended on it, inside the existing container lock.</summary>
    private static async Task RefreshDuplicatesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId,
        Guid linkId, DateTimeOffset now, CancellationToken ct)
    {
        var recheck = new List<Guid> { linkId };
        await using (var command = PostSql.Command(connection, transaction, """
            DELETE FROM harvest_post_duplicates
            WHERE container_id=@container AND (link_id=@link OR reference_link_id=@link)
            RETURNING link_id
            """, ("container", containerId), ("link", linkId)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
            {
                Guid copy = reader.GetGuid(0);
                if (copy != linkId)
                    recheck.Add(copy);
            }

        foreach (var member in recheck)
        {
            var match = await FindContentMatchAsync(connection, transaction, containerId, member, ct);
            if (match is { } confirmed)
                await PostSql.ExecuteAsync(connection, transaction, """
                    INSERT INTO harvest_post_duplicates (container_id,link_id,reference_link_id,match,similarity,matched_at)
                    VALUES (@container,@link,@reference,@match,@score,@now)
                    """, ct, ("container", containerId), ("link", member), ("reference", confirmed.LinkId),
                    ("match", confirmed.Score == 1f ? "exact" : "similar"), ("score", confirmed.Score), ("now", now));
        }
    }

    private static async Task<(Guid LinkId, float Score)?> FindContentMatchAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid containerId, Guid linkId, CancellationToken ct)
    {
        PostEnvelope incoming;
        await using (var read = PostSql.Command(connection, transaction, """
            SELECT r.fields::text
            FROM harvest_post_links l JOIN harvest_link_readings r ON r.container_id=l.container_id AND r.link_id=l.id
            WHERE l.container_id=@container AND l.id=@link AND l.role IN ('primary','alternate','copy') AND l.retired_at IS NULL
            """, ("container", containerId), ("link", linkId)))
        {
            if (await read.ExecuteScalarAsync(ct) is not string raw)
                return null;
            incoming = StoreJson.Read(raw, EmptyEnvelope());
        }

        string normalized = NormalizeText(incoming);
        if (normalized.Length < MinimumDuplicateText && incoming.Media.Count == 0)
            return null;

        // Match text is a bounded index probe. The decision below compares the source readings in full.
        int chars = Math.Min(1000, normalized.Length);
        if (chars < normalized.Length && char.IsHighSurrogate(normalized[chars - 1]))
            chars--;
        string prefix = normalized[..chars];
        var candidates = new List<(Guid Id, PostEnvelope Envelope, bool ExactProjection)>();
        await using (var command = PostSql.Command(connection, transaction, """
            WITH text_matches AS (
                SELECT t.post_id,max(similarity(t.match_text,@text)) AS score FROM harvest_post_texts t
                WHERE t.container_id=@container AND @rich AND t.match_text % @text
                GROUP BY t.post_id
            )
            SELECT l.id,r.fields::text,l.latest_content_hash=a.latest_content_hash
            FROM harvest_post_links a JOIN harvest_post_links l ON l.container_id=a.container_id AND l.url_key<>a.url_key
            JOIN harvest_posts p ON p.container_id=l.container_id AND p.id=l.post_id
            JOIN harvest_link_readings r ON r.container_id=l.container_id AND r.link_id=l.id
            LEFT JOIN text_matches t ON t.post_id=p.id
            WHERE a.container_id=@container AND a.id=@link AND l.retired_at IS NULL AND l.role IN ('primary','alternate','copy')
              AND NOT EXISTS (SELECT 1 FROM harvest_post_duplicates d WHERE d.container_id=a.container_id AND d.reference_link_id=a.id)
              AND p.merged_into IS NULL AND p.purged_at IS NULL AND l.latest_content_hash IS NOT NULL
              AND (l.latest_content_hash=a.latest_content_hash OR t.score>=@floor)
              AND NOT EXISTS (SELECT 1 FROM harvest_post_duplicates d WHERE d.link_id=l.id)
              AND NOT EXISTS (SELECT 1 FROM harvest_post_keep_apart ka WHERE ka.container_id=a.container_id
                AND ka.left_post_id=LEAST(a.post_id,p.id) AND ka.right_post_id=GREATEST(a.post_id,p.id))
            ORDER BY (l.latest_content_hash=a.latest_content_hash) DESC,t.score DESC NULLS LAST,l.first_seen_at,l.id
            LIMIT @limit
            """, ("container", containerId), ("link", linkId), ("text", prefix), ("rich", prefix.Length >= MinimumDuplicateText),
            ("floor", SimilarityFloor), ("limit", CandidateLimit)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                candidates.Add((reader.GetGuid(0), StoreJson.Read(reader.GetString(1), EmptyEnvelope()), reader.GetBoolean(2)));

        (Guid LinkId, float Score)? best = null;
        foreach (var candidate in candidates)
        {
            float score = ContentSimilarity(incoming, normalized, candidate.Envelope, candidate.ExactProjection);
            if (score >= SimilarityFloor && (best is null || score > best.Value.Score))
                best = (candidate.Id, score);
            if (score == 1)
                break;
        }
        return best;
    }

    private static float ContentSimilarity(PostEnvelope incoming, string normalized, PostEnvelope candidate, bool exactProjection)
    {
        if (!NumberSignature(incoming).AsSpan().SequenceEqual(NumberSignature(candidate))
            || !string.Equals(StoreJson.Write(incoming.Contacts), StoreJson.Write(candidate.Contacts), StringComparison.Ordinal)
            || incoming.Media.Count != candidate.Media.Count
            || !incoming.Media.Zip(candidate.Media, SameMedia).All(same => same))
            return 0;
        if (exactProjection)
            return 1;

        string reference = NormalizeText(candidate);
        if (normalized.Length < MinimumDuplicateText || reference.Length < MinimumDuplicateText)
            return 0;
        if (normalized == reference)
            return 1;
        if (normalized.Length > MaximumSimilarText || reference.Length > MaximumSimilarText
            || !DuplicateNumbers.Matches(normalized).Select(match => match.Value)
                .SequenceEqual(DuplicateNumbers.Matches(reference).Select(match => match.Value), StringComparer.Ordinal))
            return 0;

        var currentShingles = Shingles(normalized);
        var referenceShingles = Shingles(reference);
        int shared = currentShingles.Count(referenceShingles.Contains);
        int total = currentShingles.Count + referenceShingles.Count - shared;
        return total == 0 ? 0 : Math.Min(.999f, (float)shared / total);
    }

    private static bool SameMedia(PostMedia first, PostMedia second)
    {
        if (first.Kind != second.Kind)
            return false;
        if (!string.IsNullOrWhiteSpace(first.Hash) && !string.IsNullOrWhiteSpace(second.Hash))
            return string.Equals(first.Hash, second.Hash, StringComparison.OrdinalIgnoreCase);
        return string.Equals(MediaAddress(first.Url), MediaAddress(second.Url), StringComparison.Ordinal);
    }

    private static string MediaAddress(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return value;
        if (uri.IdnHost.EndsWith(".cdninstagram.com", StringComparison.Ordinal)
            || uri.IdnHost.EndsWith(".fbcdn.net", StringComparison.Ordinal))
            return uri.AbsolutePath;
        return NormalizeUrl(value);
    }

    private static HashSet<string> Shingles(string text)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var shingles = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i <= tokens.Length - 4; i++)
            shingles.Add(string.Join(' ', tokens.AsSpan(i, 4)));
        return shingles;
    }
}
