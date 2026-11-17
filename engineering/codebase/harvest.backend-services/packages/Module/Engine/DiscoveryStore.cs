using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Stores catalog checkpoints and listing identities beyond the lifetime of a run.</summary>
internal static class DiscoveryStore
{
    internal static async Task SeedCatalogAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        FrozenRun frozen, Guid run, string step, string entry, DateTimeOffset now, CancellationToken ct)
    {
        await EngineSql.ExecuteAsync(connection, transaction, """
            DELETE FROM harvest_catalog_pages
            WHERE pipeline_id=@pipeline AND (entry_url<>@entry OR NOT EXISTS (
                SELECT 1 FROM harvest_catalog_pages WHERE pipeline_id=@pipeline AND entry_url=@entry AND completed_at IS NULL));
            """, ct, ("pipeline", frozen.Pipeline.Id), ("entry", entry));
        await EngineSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_catalog_pages(pipeline_id,target_key,target,entry_url,queued_at)
            SELECT @pipeline,@key,@entry,@entry,@now WHERE NOT EXISTS (
                SELECT 1 FROM harvest_catalog_pages WHERE pipeline_id=@pipeline AND completed_at IS NULL)
            ON CONFLICT DO NOTHING
            """, ct, ("pipeline", frozen.Pipeline.Id), ("key", Key(entry)), ("entry", entry), ("now", now));

        var pages = new List<StepTarget>();
        await using (var command = EngineSql.Command(connection, transaction, """
            SELECT target,payload FROM harvest_catalog_pages WHERE pipeline_id=@pipeline AND completed_at IS NULL
            ORDER BY queued_at,target_key
            """, ("pipeline", frozen.Pipeline.Id)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                pages.Add(new StepTarget
                {
                    StepKey = step, Target = reader.GetString(0),
                    Payload = reader.IsDBNull(1) ? null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(1)),
                });
        }
        foreach (var page in pages)
            await RunPlanner.EnqueueAsync(connection, transaction, frozen, run, page, now, ct);
    }

    internal static async Task<(int Found, bool PageLimit)> CompleteCatalogAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, FrozenRun frozen, StepItem item, IReadOnlyList<StepTarget> next,
        DateTimeOffset now, CancellationToken ct)
    {
        await EngineSql.ExecuteAsync(connection, transaction, """
            UPDATE harvest_catalog_pages SET completed_at=@now WHERE pipeline_id=@pipeline AND target_key=@key
            """, ct, ("pipeline", item.PipelineId), ("key", Key(item.Target)), ("now", now));
        int found = 0;
        bool pageLimit = false;
        foreach (var target in next)
        {
            if (!RunPlanner.InScope(target.Target, frozen.Source, frozen.Pipeline.Scope, frozen.Adapter))
                continue;
            string address = Address(target.Target);
            var role = frozen.Adapter.Steps.FirstOrDefault(step => step.Key == target.StepKey)?.Role;
            if (role == StepRole.Enumerate)
            {
                int added = await EngineSql.ExecuteAsync(connection, transaction, """
                    INSERT INTO harvest_catalog_pages(pipeline_id,target_key,target,payload,entry_url,queued_at)
                    VALUES(@pipeline,@key,@target,@payload::jsonb,@entry,@now) ON CONFLICT DO NOTHING
                    """, ct, ("pipeline", item.PipelineId), ("key", Key(address)), ("target", address),
                    ("payload", target.Payload?.GetRawText()), ("entry", frozen.CatalogEntry), ("now", now));
                if (added > 0)
                    pageLimit |= await RunPlanner.EnqueueAsync(connection, transaction, frozen, item.RunId,
                        target with { Target = address }, now, ct) == RunPlanner.EnqueueResult.PageLimit;
            }
            else if (role == StepRole.Detail)
            {
                await StoreListingAsync(connection, transaction, item, target, address, now, ct);
                found++;
            }
        }
        return (found, pageLimit);
    }

    private static Task<int> StoreListingAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, StepItem item,
        StepTarget target, string address, DateTimeOffset now, CancellationToken ct)
    {
        var observation = target.Discovery;
        string externalKey = observation?.ExternalKey is { Length: > 0 and <= 500 } id ? id : "url:" + Key(address);
        return EngineSql.ExecuteAsync(connection, transaction, """
            WITH known AS (
                SELECT id FROM harvest_post_links
                WHERE container_id=@container AND source_id=@source AND external_key=@external
                  AND retired_at IS NULL AND latest_capture_id IS NOT NULL
            ), recorded AS (
                INSERT INTO harvest_discoveries AS saved
                    (id,container_id,source_id,external_key,url,title,published_at,source_modified_at,native_fields,
                     first_seen_at,last_seen_at,next_detail_at,detail_state,link_id)
                VALUES (@id,@container,@source,@external,@url,@title,@published,@modified,@native::jsonb,
                    @now,@now,@now,CASE WHEN EXISTS(SELECT 1 FROM known) THEN 'captured' ELSE 'pending' END,
                    (SELECT id FROM known))
                ON CONFLICT (container_id,source_id,external_key) DO UPDATE
                    SET url=excluded.url,
                        title=COALESCE(excluded.title,saved.title),
                        published_at=COALESCE(excluded.published_at,saved.published_at),
                        source_modified_at=GREATEST(excluded.source_modified_at,saved.source_modified_at),
                        native_fields=COALESCE(excluded.native_fields,saved.native_fields),
                        last_seen_at=excluded.last_seen_at,
                        revision=saved.revision+CASE WHEN saved.url<>excluded.url
                            OR excluded.source_modified_at>COALESCE(saved.source_modified_at,'-infinity')
                            OR (excluded.native_fields IS NOT NULL AND saved.native_fields IS DISTINCT FROM excluded.native_fields)
                            THEN 1 ELSE 0 END,
                        detail_state=CASE WHEN saved.url<>excluded.url
                            OR excluded.source_modified_at>COALESCE(saved.source_modified_at,'-infinity')
                            OR (excluded.native_fields IS NOT NULL AND saved.native_fields IS DISTINCT FROM excluded.native_fields)
                            THEN 'pending' ELSE saved.detail_state END,
                        next_detail_at=CASE WHEN saved.detail_state IN ('captured','gone') THEN excluded.next_detail_at ELSE saved.next_detail_at END
                RETURNING id
            )
            INSERT INTO harvest_discovery_scopes(container_id,pipeline_id,discovery_id,catalog_url,last_seen_at)
            SELECT @container,@pipeline,id,@catalog,@now FROM recorded
            ON CONFLICT(pipeline_id,discovery_id) DO UPDATE SET catalog_url=excluded.catalog_url,last_seen_at=excluded.last_seen_at
            """, ct, ("id", Guid.CreateVersion7(now)), ("container", item.ContainerId), ("source", item.SourceId),
            ("pipeline", item.PipelineId), ("external", externalKey), ("url", address), ("catalog", item.Target), ("now", now),
            ("title", observation?.Title), ("published", observation?.PublishedAtUtc?.ToUniversalTime()),
            ("modified", observation?.LastModifiedUtc?.ToUniversalTime()), ("native", observation?.NativeFields?.GetRawText()));
    }

    internal static async Task SeedDetailsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, FrozenRun frozen,
        Guid run, string step, DateTimeOffset now, CancellationToken ct)
    {
        var candidates = new List<(Guid Id, string Url, string ExternalKey, long Revision)>();
        await using (var command = EngineSql.Command(connection, transaction, """
            SELECT d.id,d.url,d.external_key,d.revision FROM harvest_discoveries d
            JOIN harvest_discovery_scopes s ON s.discovery_id=d.id AND s.pipeline_id=@pipeline
            WHERE d.container_id=@container AND d.source_id=@source AND d.detail_state IN ('pending','failed')
              AND d.next_detail_at<=@now AND NOT EXISTS (
                SELECT 1 FROM harvest_runs r WHERE r.id=d.reserved_run_id AND r.status IN ('queued','running','paused'))
            ORDER BY d.next_detail_at,d.id LIMIT @batch FOR UPDATE OF d SKIP LOCKED
            """, ("pipeline", frozen.Pipeline.Id), ("container", frozen.Pipeline.ContainerId), ("source", frozen.Source.Id),
            ("now", now), ("batch", frozen.Stage!.BatchSize)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                candidates.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)));
        }
        foreach (var candidate in candidates)
        {
            var target = new StepTarget
            {
                StepKey = step, Target = candidate.Url,
                Payload = JsonSerializer.SerializeToElement(new { discoveryId = candidate.Id, candidate.Revision, externalKey = candidate.ExternalKey },
                    WoW.Two.Sdk.Beta.Be.Foundation.Serialization.JsonOptionsConstants.Default),
            };
            if (await RunPlanner.EnqueueAsync(connection, transaction, frozen, run, target, now, ct) == RunPlanner.EnqueueResult.Enqueued)
                await EngineSql.ExecuteAsync(connection, transaction,
                    "UPDATE harvest_discoveries SET reserved_run_id=@run WHERE id=@id", ct, ("run", run), ("id", candidate.Id));
        }
    }

    internal static Task<int> CompleteDetailAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, StepItem item,
        FetchOutcome outcome, Guid? link, DateTimeOffset now, DateTimeOffset? retry, CancellationToken ct)
    {
        if (item.Payload is not { } payload
            || !payload.TryGetProperty("discoveryId", out var discovered) || !discovered.TryGetGuid(out var id))
            return Task.FromResult(0);
        long revision = payload.GetProperty("revision").GetInt64();
        string state = outcome switch
        {
            FetchOutcome.Ok => "captured",
            FetchOutcome.Gone => "gone",
            _ => "failed",
        };
        return EngineSql.ExecuteAsync(connection, transaction, """
            UPDATE harvest_discoveries
            SET detail_state=CASE WHEN revision=@revision THEN @state ELSE 'pending' END,
                link_id=COALESCE(@link,link_id),next_detail_at=CASE WHEN revision=@revision THEN @next ELSE @now END,
                reserved_run_id=NULL
            WHERE id=@id AND container_id=@container AND source_id=@source AND reserved_run_id=@run
            """, ct, ("state", state), ("link", link), ("next", retry ?? now.AddMinutes(5)), ("now", now), ("revision", revision),
            ("id", id), ("container", item.ContainerId), ("source", item.SourceId), ("run", item.RunId));
    }

    internal static string Address(string url) =>
        new Uri(url).GetComponents(UriComponents.AbsoluteUri & ~UriComponents.Fragment, UriFormat.UriEscaped);

    internal static string Key(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
