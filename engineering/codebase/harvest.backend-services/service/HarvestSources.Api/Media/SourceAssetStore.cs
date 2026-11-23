using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace HarvestSources.Api.Media;

/// <summary>Maintains durable, source-scoped media jobs and their private Files references.</summary>
/// <remarks>A web source's images queue from its own host, and from the image domains its adapter declares
/// (<see cref="SourceMediaRules"/>), up to its declared limit of images per post, else <see cref="SourceConstants.MaxPostImages"/>.</remarks>
internal sealed class SourceAssetStore([FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource database, SourceMediaRules mediaRules)
{
    public async Task QueueMissingAsync(CancellationToken ct)
    {
        var rules = await mediaRules.ListAsync(ct);
        // A declared domain matches only a plain https authority (letters, digits, dots and hyphens), so a URL with
        // userinfo or an explicit port never reads as a host.
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("""
            INSERT INTO harvest_source_assets(id,source_id,kind,origin_url)
            SELECT gen_random_uuid(),s.id,'favicon','https://' || s.handle || '/'
            FROM harvest_sources s
            WHERE s.family IN ('site','page','structured','render','feed','listing') AND s.state='active'
                AND s.permission IN ('open','granted') AND s.posture='own_fetch'
                AND EXISTS (SELECT 1 FROM harvest_pipelines p WHERE p.source_id=s.id)
            ON CONFLICT DO NOTHING;

            WITH rules AS (
                SELECT r.source_id,r.domain,r.max_images
                FROM unnest(@rule_sources,@rule_domains,@rule_limits) AS r(source_id,domain,max_images)
            ), limits AS (
                SELECT source_id,min(max_images) AS max_images FROM rules GROUP BY source_id
            ), urls AS (
                SELECT DISTINCT l.source_id,m.value->>'url' AS url
                FROM harvest_posts p JOIN harvest_post_links l ON l.container_id=p.container_id AND l.post_id=p.id
                JOIN harvest_sources s ON s.id=l.source_id
                LEFT JOIN limits lim ON lim.source_id=s.id
                CROSS JOIN LATERAL jsonb_array_elements(CASE WHEN jsonb_typeof(p.base->'media')='array'
                    THEN p.base->'media' ELSE '[]'::jsonb END) WITH ORDINALITY m(value,position)
                CROSS JOIN LATERAL (SELECT lower(substring(m.value->>'url' from '^https://([A-Za-z0-9.-]+)/')) AS host) h
                WHERE s.state='active' AND s.permission IN ('open','granted')
                    AND p.merged_into IS NULL AND p.state<>'gone' AND l.retired_at IS NULL
                    AND l.first_seen_at >= (SELECT collect_after FROM harvest_source_image_policy WHERE singleton)
                    AND m.value->>'kind'='image' AND m.position<=COALESCE(lim.max_images,@images)
                    AND length(m.value->>'url')<=2000
                    AND ((s.family IN ('site','page','structured','render','feed','listing') AND s.posture='own_fetch'
                            AND (starts_with(m.value->>'url','https://' || s.handle || '/')
                                OR EXISTS (SELECT 1 FROM rules r WHERE r.source_id=s.id
                                    AND (h.host=r.domain OR right(h.host,length(r.domain)+1)='.' || r.domain))))
                        OR (s.family='instagram' AND s.posture='official'
                            AND m.value->>'url' ~* '^https://([a-z0-9-]+\.)+(cdninstagram\.com|fbcdn\.net)(:443)?/'
                            AND position('access_token' in lower(m.value->>'url'))=0))
            )
            INSERT INTO harvest_source_assets(id,source_id,kind,origin_url)
            SELECT gen_random_uuid(),u.source_id,'image',u.url FROM urls u
            WHERE NOT EXISTS (SELECT 1 FROM harvest_source_assets a WHERE a.source_id=u.source_id
                AND a.kind='image' AND a.origin_url=u.url)
            ORDER BY u.source_id,u.url LIMIT 500
            ON CONFLICT DO NOTHING
            """, connection);
        command.Parameters.AddWithValue("images", SourceConstants.MaxPostImages);
        AddRules(command, rules);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<SourceAsset?> ClaimAsync(CancellationToken ct)
    {
        var rules = await mediaRules.ListAsync(ct);
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("""
            WITH limits AS (
                SELECT r.source_id,min(r.max_images) AS max_images
                FROM unnest(@rule_sources,@rule_domains,@rule_limits) AS r(source_id,domain,max_images) GROUP BY r.source_id
            ), next AS (
                SELECT a.id,s.handle,s.family FROM harvest_source_assets a JOIN harvest_sources s ON s.id=a.source_id
                LEFT JOIN harvest_host_state h ON h.host_key=CASE WHEN s.family='instagram' THEN 'www.instagram.com' ELSE s.handle END
                WHERE a.file_id IS NULL AND a.retry_at<=now() AND s.state='active'
                    AND s.permission IN ('open','granted')
                    AND ((s.family IN ('site','page','structured','render','feed','listing') AND s.posture='own_fetch')
                        OR (s.family='instagram' AND s.posture='official' AND a.kind='image'))
                    AND (a.kind='favicon' OR EXISTS (
                        SELECT 1 FROM harvest_posts p JOIN harvest_post_links l ON l.container_id=p.container_id AND l.post_id=p.id
                        CROSS JOIN LATERAL jsonb_array_elements(CASE WHEN jsonb_typeof(p.base->'media')='array'
                            THEN p.base->'media' ELSE '[]'::jsonb END) WITH ORDINALITY m(value,position)
                        WHERE l.source_id=a.source_id AND l.retired_at IS NULL AND p.merged_into IS NULL AND p.state<>'gone'
                            AND m.value->>'kind'='image' AND m.value->>'url'=a.origin_url
                            AND m.position<=COALESCE((SELECT lim.max_images FROM limits lim WHERE lim.source_id=a.source_id),@images)
                            -- Source catalog (CatalogGate.PostOff, which this host cannot reach): a post whose every link
                            -- is a source the instance turned off is not fetched; its copy stays queued and unclaimed until one is on.
                            AND EXISTS (SELECT 1 FROM harvest_post_links gl JOIN harvest_sources gs ON gs.id=gl.source_id
                                WHERE gl.container_id=p.container_id AND gl.post_id=p.id AND gl.retired_at IS NULL
                                    AND NOT EXISTS (SELECT 1 FROM harvest_catalog_sources cg WHERE cg.key=gs.catalog_key AND NOT cg.turned_on))))
                    AND (h.paused_until IS NULL OR h.paused_until<=now())
                    AND (h.retry_after_until IS NULL OR h.retry_after_until<=now())
                ORDER BY CASE WHEN a.kind='favicon' THEN 0 ELSE 1 END,a.retry_at,a.id
                LIMIT 1 FOR UPDATE OF a SKIP LOCKED
            )
            UPDATE harvest_source_assets a SET retry_at=now()+interval '15 minutes'
            FROM next WHERE a.id=next.id RETURNING a.id,a.source_id,a.kind,a.origin_url,next.handle,next.family
            """, connection);
        command.Parameters.AddWithValue("images", SourceConstants.MaxPostImages);
        AddRules(command, rules);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new SourceAsset(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2) == "favicon", reader.GetString(3), reader.GetString(4), reader.GetString(5) == "instagram")
            : null;
    }

    /// <summary>Passes the declared media rules as parallel arrays, one entry per source and domain, so no SQL is built from them.</summary>
    private static void AddRules(NpgsqlCommand command, IReadOnlyCollection<SourceMediaRule> rules)
    {
        var entries = rules.SelectMany(rule => rule.Domains.Select(domain => (rule.SourceId, Domain: domain, rule.Limit))).ToArray();
        command.Parameters.AddWithValue("rule_sources", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid, entries.Select(entry => entry.SourceId).ToArray());
        command.Parameters.AddWithValue("rule_domains", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text, entries.Select(entry => entry.Domain).ToArray());
        command.Parameters.AddWithValue("rule_limits", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer, entries.Select(entry => entry.Limit).ToArray());
    }

    /// <summary>Records how an attempt ended: the retry time, the outcome code (<see cref="SourceAssetOutcomes"/>), when it finished
    /// and, for a copy, the bytes uploaded.</summary>
    public async Task FinishAsync(Guid id, Guid? fileId, TimeSpan retry, string outcome, long? bytes, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("""
            UPDATE harvest_source_assets SET file_id=@file,retry_at=now()+@retry,outcome=@outcome,bytes=@bytes,finished_at=now()
            WHERE id=@id AND file_id IS NULL
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("file", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)fileId ?? DBNull.Value);
        command.Parameters.AddWithValue("retry", retry);
        command.Parameters.AddWithValue("outcome", outcome);
        command.Parameters.AddWithValue("bytes", NpgsqlTypes.NpgsqlDbType.Bigint, (object?)bytes ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<(Guid FileId, bool Favicon)?> FindAsync(Guid sourceId, Guid assetId, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT file_id,kind FROM harvest_source_assets WHERE source_id=@source AND id=@id AND file_id IS NOT NULL
            """, connection);
        command.Parameters.AddWithValue("source", sourceId);
        command.Parameters.AddWithValue("id", assetId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.GetGuid(0), reader.GetString(1) == "favicon") : null;
    }
}

internal sealed record SourceAsset(Guid Id, Guid SourceId, bool Favicon, string Url, string Host, bool Instagram);

/// <summary>The outcome codes a copy attempt records in <c>harvest_source_assets.outcome</c>, one per way <see cref="SourceAssetWorker"/>'s attempt can end.</summary>
internal static class SourceAssetOutcomes
{
    /// <summary>The bytes were uploaded to Files.</summary>
    public const string Copied = "copied";

    /// <summary>The URL is not parseable or the destination policy refuses it.</summary>
    public const string Rejected = "rejected";

    /// <summary>The final URL, after redirects, fails the destination policy.</summary>
    public const string RedirectedOffPolicy = "redirected_off_policy";

    /// <summary>The response is not a supported image type.</summary>
    public const string UnsupportedType = "unsupported_type";

    /// <summary>The response has no body.</summary>
    public const string Empty = "empty";

    /// <summary>The body is over the size limit.</summary>
    public const string TooLarge = "too_large";

    /// <summary>The Files upload failed.</summary>
    public const string FilesDeclined = "files_declined";

    /// <summary>The fetch did not return <see cref="FetchOutcome.Ok"/>: <c>fetch_</c> and the outcome's name in lower snake case
    /// (<c>fetch_gone</c>, <c>fetch_robots_disallowed</c>, <c>fetch_rate_limited</c>).</summary>
    public static string Fetch(FetchOutcome outcome) => "fetch_" + JsonNamingPolicy.SnakeCaseLower.ConvertName(outcome.ToString());
}
