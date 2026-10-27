using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using Capture = WoW.Two.Sdk.Beta.Be.Harvest.Readers.Capture;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

internal sealed partial class ModulePostBroker
{
    /// <inheritdoc />
    public async Task<Result<PostModel>> GetPostAsync(Guid containerId, Guid postId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var post = await ReadPostAsync(connection, null, containerId, postId, cancellationToken);
        return post is null ? Missing<PostModel>("Post not found in the container.") : Result<PostModel>.Ok(post);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PostModel>>> ListPostsAsync(Guid containerId, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
            return Invalid<IReadOnlyList<PostModel>>("The limit must be between 1 and 500.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (await PostSql.ScalarAsync(connection, null, "SELECT 1 FROM harvest_containers WHERE id=@id", cancellationToken, ("id", containerId)) is null)
            return Missing<IReadOnlyList<PostModel>>("Container not found.");
        var ids = new List<Guid>();
        await using (var command = PostSql.Command(connection, null,
                         "SELECT id FROM harvest_posts WHERE container_id=@container AND merged_into IS NULL ORDER BY last_changed_at DESC,id DESC LIMIT @limit",
                         ("container", containerId), ("limit", limit)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
        var posts = new List<PostModel>();
        foreach (var id in ids)
            posts.Add((await ReadPostAsync(connection, null, containerId, id, cancellationToken))!);
        return Result<IReadOnlyList<PostModel>>.Ok(posts);
    }

    internal static async Task<PostModel?> ReadPostAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid containerId, Guid postId, CancellationToken ct)
    {
        PostModel post;
        await using (var command = PostSql.Command(connection, transaction, """
                         SELECT id,state,revision,base::text,field_prov::text,first_seen_at,last_changed_at,merged_into
                         FROM harvest_posts WHERE container_id=@container AND id=@post
                         """, ("container", containerId), ("post", postId)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return null;
            post = new PostModel
            {
                Id = reader.GetGuid(0), ContainerId = containerId, State = Enum.Parse<PostState>(reader.GetString(1), true), Revision = reader.GetInt32(2),
                Base = StoreJson.Read(reader.GetString(3), EmptyEnvelope()),
                FieldSources = StoreJson.Read(reader.GetString(4), new Dictionary<string, Guid>()),
                FirstSeenAtUtc = reader.GetFieldValue<DateTimeOffset>(5), LastChangedAtUtc = reader.GetFieldValue<DateTimeOffset>(6),
                MergedInto = reader.IsDBNull(7) ? null : reader.GetGuid(7),
            };
        }
        var links = new List<PostLinkModel>();
        await using (var command = PostSql.Command(connection, transaction, """
                         SELECT id,source_id,external_key,url,state,role,latest_capture_id,latest_captured_at FROM harvest_post_links
                         WHERE container_id=@container AND post_id=@post AND retired_at IS NULL ORDER BY first_seen_at,id
                         """, ("container", containerId), ("post", postId)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                links.Add(new PostLinkModel
                {
                    Id = reader.GetGuid(0), SourceId = reader.GetGuid(1), ExternalKey = reader.GetString(2), Url = reader.GetString(3),
                    State = Enum.Parse<LinkState>(reader.GetString(4), true), Role = Enum.Parse<LinkRole>(reader.GetString(5), true),
                    CaptureId = reader.IsDBNull(6) ? null : reader.GetGuid(6), CapturedAtUtc = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                });
        return post with { Links = links };
    }

    private static async Task<Guid?> FindDuplicateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId, Capture capture, PostEnvelope envelope, byte[] contentHash, CancellationToken ct)
    {
        if (capture.Role is LinkRole.Part or LinkRole.Reply) return null;
        var normalized = NormalizeText(envelope);
        var candidates = new List<Guid>();
        await using var command = PostSql.Command(connection, transaction, """
            SELECT DISTINCT p.id FROM harvest_posts p JOIN harvest_post_links l ON l.post_id=p.id
            LEFT JOIN harvest_post_texts t ON t.post_id=p.id
            LEFT JOIN harvest_link_readings r ON r.link_id=l.id
            WHERE p.container_id=@container AND p.merged_into IS NULL AND p.purged_at IS NULL
              AND l.retired_at IS NULL AND l.role IN ('primary','alternate','copy')
              AND NOT (CAST(@origin AS text) IS NOT NULL AND l.origin_key IS NOT NULL AND l.origin_key<>@origin)
              AND (l.url_key=@url OR (CAST(@origin AS text) IS NOT NULL AND l.origin_key=@origin)
                   OR (p.kind=@kind AND t.num_sig=@signature AND r.contacts=CAST(@contacts AS jsonb)
                       AND ((@rich AND l.latest_content_hash=@hash) OR (@textRich AND t.text_norm_hash=@text))))
              AND NOT EXISTS(SELECT 1 FROM harvest_post_keep_apart k WHERE k.container_id=p.container_id AND (k.left_post_id=p.id OR k.right_post_id=p.id))
            ORDER BY p.id LIMIT 2
            """, ("container", containerId), ("url", MD5.HashData(Encoding.UTF8.GetBytes(NormalizeUrl(capture.Url)))),
            ("origin", string.IsNullOrWhiteSpace(capture.OriginKey) ? null : capture.OriginKey), ("kind", Kind(envelope)),
            ("rich", capture.ContentProjection.Length >= 64), ("hash", contentHash), ("textRich", normalized.Length >= 64),
            ("text", MD5.HashData(Encoding.UTF8.GetBytes(normalized))), ("signature", NumberSignature(envelope)), ("contacts", StoreJson.Write(envelope.Contacts)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) candidates.Add(reader.GetGuid(0));
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static async Task ResolveAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId, Guid postId, DateTimeOffset now, bool increment, CancellationToken ct)
    {
        var readings = new List<(Guid LinkId, PostEnvelope Envelope)>();
        await using (var command = PostSql.Command(connection, transaction, """
                         SELECT l.id,r.fields::text FROM harvest_post_links l JOIN harvest_sources s ON s.id=l.source_id
                         JOIN harvest_link_readings r ON r.link_id=l.id
                         WHERE l.container_id=@container AND l.post_id=@post AND l.retired_at IS NULL
                         ORDER BY CASE WHEN l.state IN ('live','relisted') THEN 0 ELSE 1 END,s.authority DESC,r.captured_at DESC,l.id
                         """, ("container", containerId), ("post", postId)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) readings.Add((reader.GetGuid(0), StoreJson.Read(reader.GetString(1), EmptyEnvelope())));
        if (readings.Count == 0) return;

        var (resolved, provenance) = ResolveFields(readings);
        await PostSql.ExecuteAsync(connection, transaction, """
            WITH rollup AS (SELECT CASE
                WHEN NOT EXISTS(SELECT 1 FROM harvest_post_links WHERE post_id=@post AND retired_at IS NULL AND check_capability<>'none') THEN 'live'
                WHEN EXISTS(SELECT 1 FROM harvest_post_links WHERE post_id=@post AND retired_at IS NULL AND check_capability<>'none' AND state IN ('live','relisted')) THEN 'live'
                WHEN EXISTS(SELECT 1 FROM harvest_post_links WHERE post_id=@post AND retired_at IS NULL AND check_capability<>'none' AND state='suspect') THEN 'suspect'
                ELSE 'gone' END AS state)
            UPDATE harvest_posts p SET state_since=CASE WHEN p.state<>rollup.state THEN @now ELSE p.state_since END,
                state=rollup.state FROM rollup WHERE p.container_id=@container AND p.id=@post
            """, ct, ("post", postId), ("now", now), ("container", containerId));
        await PostSql.ExecuteAsync(connection, transaction, """
            UPDATE harvest_posts SET base=CAST(@base AS jsonb),field_prov=CAST(@prov AS jsonb),kind=@kind,
                revision=revision+@bump,last_changed_at=@now,dedup_state='settled',primary_locale=@locale
            WHERE container_id=@container AND id=@post;
            INSERT INTO harvest_post_texts(post_id,container_id,locale,title,body,match_text,text_norm_hash,num_sig,link_id,updated_at)
            VALUES(@post,@container,@textLocale,@title,@text,@match,@hash,@signature,@link,@now)
            ON CONFLICT(post_id,locale) DO UPDATE SET title=EXCLUDED.title,body=EXCLUDED.body,match_text=EXCLUDED.match_text,
                text_norm_hash=EXCLUDED.text_norm_hash,num_sig=EXCLUDED.num_sig,link_id=EXCLUDED.link_id,updated_at=EXCLUDED.updated_at
            """, ct, ("base", StoreJson.Write(resolved)), ("prov", StoreJson.Write(provenance)), ("kind", Kind(resolved)), ("bump", increment ? 1 : 0),
            ("now", now), ("container", containerId), ("post", postId), ("locale", resolved.Language?.Code),
            ("textLocale", resolved.Language?.Code ?? "und"), ("title", resolved.Title), ("text", resolved.Text),
            ("match", NormalizeText(resolved)[..Math.Min(1000, NormalizeText(resolved).Length)]),
            ("hash", MD5.HashData(Encoding.UTF8.GetBytes(NormalizeText(resolved)))), ("signature", NumberSignature(resolved)), ("link", readings[0].LinkId));
    }

    private static (PostEnvelope Envelope, IReadOnlyDictionary<string, Guid> Provenance) ResolveFields(IReadOnlyList<(Guid LinkId, PostEnvelope Envelope)> readings)
    {
        var values = new Dictionary<string, JsonElement>();
        var provenance = new Dictionary<string, Guid>();
        var extractionSources = new Dictionary<string, FieldSource>();
        foreach (var (linkId, envelope) in readings)
        {
            var json = JsonSerializer.SerializeToElement(envelope, WoW.Two.Sdk.Beta.Be.Foundation.Serialization.JsonOptionsConstants.Default);
            foreach (var field in json.EnumerateObject())
            {
                if (field.Name == "fieldSources" || values.ContainsKey(field.Name) || !HasValue(field.Value)) continue;
                values.Add(field.Name, field.Value.Clone());
                if (field.Name is not "schema" and not "extraction" and not "fieldSources") provenance.Add(field.Name, linkId);
                if (envelope.FieldSources.TryGetValue(field.Name, out var extractionSource)) extractionSources[field.Name] = extractionSource;
            }
        }
        values["fieldSources"] = JsonSerializer.SerializeToElement(extractionSources, WoW.Two.Sdk.Beta.Be.Foundation.Serialization.JsonOptionsConstants.Default);
        return (StoreJson.Read(StoreJson.Write(values), readings[0].Envelope), provenance);
    }

    private static bool HasValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => false,
        JsonValueKind.Array => value.GetArrayLength() > 0,
        JsonValueKind.Object => value.EnumerateObject().Any(),
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        _ => true,
    };

    private static string NormalizeText(PostEnvelope value) => Regex.Replace(
        $"{value.Title}\n{value.Text}".Normalize(NormalizationForm.FormC).ToLowerInvariant(), @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100)).Trim();

    private static byte[] NumberSignature(PostEnvelope value) => MD5.HashData(Encoding.UTF8.GetBytes(StoreJson.Write(new
    {
        value.Dates, value.Prices,
    })));

    private static PostEnvelope EmptyEnvelope() => new() { Extraction = new ExtractionStamp { NormVersion = "unknown", RulesVersion = "unknown" } };
}
