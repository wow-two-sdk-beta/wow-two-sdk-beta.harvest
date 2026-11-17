using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace HarvestSources.Api.Media;

/// <summary>Maintains durable, source-scoped media jobs and their private Files references.</summary>
internal sealed class SourceAssetStore([FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource database)
{
    public async Task QueueMissingAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("""
            INSERT INTO harvest_source_assets(id,source_id,kind,origin_url)
            SELECT gen_random_uuid(),s.id,'favicon','https://' || s.handle || '/'
            FROM harvest_sources s
            WHERE s.family IN ('site','page','structured','render','feed','listing') AND s.state='active'
                AND s.permission IN ('open','granted') AND s.posture='own_fetch'
                AND EXISTS (SELECT 1 FROM harvest_pipelines p WHERE p.source_id=s.id)
            ON CONFLICT DO NOTHING;

            WITH urls AS (
                SELECT DISTINCT l.source_id,m.value->>'url' AS url
                FROM harvest_posts p JOIN harvest_post_links l ON l.container_id=p.container_id AND l.post_id=p.id
                JOIN harvest_sources s ON s.id=l.source_id
                CROSS JOIN LATERAL jsonb_array_elements(CASE WHEN jsonb_typeof(p.base->'media')='array'
                    THEN p.base->'media' ELSE '[]'::jsonb END) WITH ORDINALITY m(value,position)
                WHERE s.state='active' AND s.permission IN ('open','granted')
                    AND p.merged_into IS NULL AND p.state<>'gone' AND l.retired_at IS NULL
                    AND m.value->>'kind'='image' AND m.position<=@images
                    AND length(m.value->>'url')<=2000
                    AND ((s.family IN ('site','page','structured','render','feed','listing') AND s.posture='own_fetch'
                            AND starts_with(m.value->>'url','https://' || s.handle || '/'))
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
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<SourceAsset?> ClaimAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("""
            WITH next AS (
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
                            AND m.value->>'kind'='image' AND m.position<=@images AND m.value->>'url'=a.origin_url))
                    AND (h.paused_until IS NULL OR h.paused_until<=now())
                    AND (h.retry_after_until IS NULL OR h.retry_after_until<=now())
                ORDER BY CASE WHEN a.kind='favicon' THEN 0 ELSE 1 END,a.retry_at,a.id
                LIMIT 1 FOR UPDATE OF a SKIP LOCKED
            )
            UPDATE harvest_source_assets a SET retry_at=now()+interval '15 minutes'
            FROM next WHERE a.id=next.id RETURNING a.id,a.kind,a.origin_url,next.handle,next.family
            """, connection);
        command.Parameters.AddWithValue("images", SourceConstants.MaxPostImages);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new SourceAsset(reader.GetGuid(0), reader.GetString(1) == "favicon", reader.GetString(2), reader.GetString(3), reader.GetString(4) == "instagram")
            : null;
    }

    public async Task FinishAsync(Guid id, Guid? fileId, TimeSpan retry, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("""
            UPDATE harvest_source_assets SET file_id=@file,retry_at=now()+@retry WHERE id=@id AND file_id IS NULL
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("file", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)fileId ?? DBNull.Value);
        command.Parameters.AddWithValue("retry", retry);
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

internal sealed record SourceAsset(Guid Id, bool Favicon, string Url, string Host, bool Instagram);
