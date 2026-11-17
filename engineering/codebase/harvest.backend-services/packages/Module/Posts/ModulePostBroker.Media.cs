using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

internal sealed partial class ModulePostBroker
{
    /// <summary>Refreshes expiring media references without creating a content revision or another delivery event.</summary>
    private static Task RefreshMediaAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid containerId, Guid postId, Guid linkId, PostEnvelope envelope, DateTimeOffset capturedAt,
        DateTimeOffset now, CancellationToken ct) => PostSql.ExecuteAsync(connection, transaction, """
            WITH refreshed AS (
                UPDATE harvest_link_readings r
                SET fields=jsonb_set(r.fields,'{media}',CAST(@media AS jsonb)),updated_at=@now
                FROM harvest_link_schedule s
                WHERE r.container_id=@container AND r.link_id=@link AND s.link_id=r.link_id
                    AND s.container_id=r.container_id AND s.last_seen_at<=@at
                    AND r.fields->'media' IS DISTINCT FROM CAST(@media AS jsonb)
                RETURNING r.link_id
            )
            UPDATE harvest_posts p SET base=jsonb_set(p.base,'{media}',CAST(@media AS jsonb))
            FROM refreshed r WHERE p.container_id=@container AND p.id=@post
                AND p.field_prov->>'media'=r.link_id::text;

            WITH candidate AS (
                SELECT l.id,r.fields->'media' AS media
                FROM harvest_post_links l JOIN harvest_sources s ON s.id=l.source_id
                JOIN harvest_link_readings r ON r.link_id=l.id
                WHERE l.container_id=@container AND l.post_id=@post AND l.retired_at IS NULL
                    AND jsonb_typeof(r.fields->'media')='array' AND r.fields->'media'<>'[]'::jsonb
                ORDER BY CASE WHEN l.state IN ('live','relisted') THEN 0 ELSE 1 END,
                    s.authority DESC,r.captured_at DESC,l.id
                LIMIT 1
            )
            UPDATE harvest_posts p
            SET base=jsonb_set(p.base,'{media}',c.media),
                field_prov=jsonb_set(p.field_prov,'{media}',to_jsonb(c.id::text))
            FROM candidate c WHERE p.container_id=@container AND p.id=@post
                AND NOT (p.field_prov ? 'media')
                AND (p.base->'media' IS NULL OR p.base->'media'='[]'::jsonb)
            """, ct, ("container", containerId), ("post", postId), ("link", linkId),
            ("media", StoreJson.Write(envelope.Media)), ("at", capturedAt), ("now", now));
}
