using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

/// <summary>Integrates immutable captures and resolved posts over the module's keyed PostgreSQL store.</summary>
internal sealed partial class ModulePostBroker(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource,
    TimeProvider time,
    IEnumerable<IDeliveryPostFilter> filters) : IPostBroker
{
    private readonly DeliveryPostFilters postFilters = new(filters);

    /// <inheritdoc />
    public async Task<Result<CaptureSaveResult>> SaveCaptureAsync(Guid containerId, Guid sourceId, Guid? runId, Capture capture, PostEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (capture is null || envelope?.Extraction is null || capture.ContentProjection is null)
            return Invalid<CaptureSaveResult>("A capture and an envelope with its extraction stamp are required.");
        if (string.IsNullOrWhiteSpace(capture.ExternalKey) || !IsWebUrl(capture.Url) || !IsWebUrl(capture.RequestUrl) || !IsWebUrl(capture.FinalUrl) || !Enum.IsDefined(capture.Role))
            return Invalid<CaptureSaveResult>("A capture requires a source-local key and absolute HTTP(S) URLs.");
        if (capture.CapturedAtUtc.Offset != TimeSpan.Zero)
            return Invalid<CaptureSaveResult>("CapturedAtUtc must use UTC.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostSql.LockAsync(connection, transaction, containerId, cancellationToken);
        if (await PostSql.ScalarAsync(connection, transaction,
                "SELECT 1 FROM harvest_sources WHERE container_id=@container AND id=@source", cancellationToken,
                ("container", containerId), ("source", sourceId)) is null)
            return Missing<CaptureSaveResult>("Source not found in the container.");
        if (runId is not null && await PostSql.ScalarAsync(connection, transaction,
                "SELECT 1 FROM harvest_runs r JOIN harvest_pipelines p ON p.id=r.pipeline_id WHERE r.container_id=@container AND r.id=@run AND p.source_id=@source", cancellationToken,
                ("container", containerId), ("run", runId), ("source", sourceId)) is null)
            return Invalid<CaptureSaveResult>("The capture's run must belong to its source and container.");

        var now = Now();
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes(capture.ContentProjection));
        var link = await FindLinkAsync(connection, transaction, containerId, sourceId, capture.ExternalKey, cancellationToken);
        var isNewPost = false;
        var newLink = link is null;
        if (link is null)
        {
            var postId = await FindDuplicateAsync(connection, transaction, containerId, capture, envelope, contentHash, cancellationToken);
            if (postId is null)
            {
                postId = Guid.CreateVersion7(now);
                isNewPost = true;
                await PostSql.ExecuteAsync(connection, transaction, """
                    INSERT INTO harvest_posts(id,container_id,kind,state_since,first_seen_at,last_changed_at,base,dedup_state)
                    VALUES(@id,@container,@kind,@now,@now,@now,CAST(@base AS jsonb),'settled')
                    """, cancellationToken, ("id", postId.Value), ("container", containerId),
                    ("kind", Kind(envelope)), ("now", now), ("base", StoreJson.Write(envelope)));
            }
            var linkId = Guid.CreateVersion7(now);
            await PostSql.ExecuteAsync(connection, transaction, """
                INSERT INTO harvest_post_links(id,container_id,source_id,post_id,external_key,url,url_key,origin_key,role,locale,state_since,first_seen_at,last_changed_at,check_capability)
                SELECT @id,@container,@source,@post,@external,@url,@key,@origin,@role,@locale,@now,@now,@now,
                    CASE WHEN cp.key='none' THEN 'none' ELSE 'full' END
                FROM harvest_sources s LEFT JOIN harvest_check_policies cp ON cp.id=s.check_policy_id WHERE s.id=@source
                """, cancellationToken, ("id", linkId), ("container", containerId), ("source", sourceId),
                ("post", postId.Value), ("external", capture.ExternalKey), ("url", capture.Url),
                ("key", MD5.HashData(Encoding.UTF8.GetBytes(NormalizeUrl(capture.Url)))),
                ("origin", capture.OriginKey), ("role", capture.Role.ToString().ToLowerInvariant()), ("locale", capture.Locale), ("now", now));
            await PostSql.ExecuteAsync(connection, transaction,
                "INSERT INTO harvest_link_schedule(link_id,container_id,next_check_at,last_seen_at) VALUES(@id,@container,@next,@now)", cancellationToken,
                ("id", linkId), ("container", containerId), ("next", capture.CapturedAtUtc.AddHours(6)), ("now", capture.CapturedAtUtc));
            link = new LinkRow(linkId, postId.Value, null, LinkState.Live, null, now);
        }

        if ((link.CapturedAtUtc is { } latest && capture.CapturedAtUtc < latest)
            || (link.State is LinkState.Suspect or LinkState.Gone && capture.CapturedAtUtc <= link.StateSinceUtc))
        {
            var newer = (await ReadPostAsync(connection, transaction, containerId, link.PostId, cancellationToken))!;
            await transaction.CommitAsync(cancellationToken);
            return Result<CaptureSaveResult>.Ok(new CaptureSaveResult { Post = newer, LinkId = link.Id, CaptureId = null, Changed = false });
        }
        var changed = link.Hash is null || !link.Hash.AsSpan().SequenceEqual(contentHash);
        var restored = link.State is not LinkState.Live and not LinkState.Relisted && capture.CapturedAtUtc > link.StateSinceUtc;
        Guid? captureId = null;
        if (changed)
        {
            captureId = Guid.CreateVersion7(now);
            await PostSql.ExecuteAsync(connection, transaction, """
                INSERT INTO harvest_captures(id,container_id,source_id,link_id,run_id,captured_at,request_url,final_url,http_status,route,posture,
                    raw_hash,content_hash,body_inline,body_bytes,content_type,retention_class,expires_at,warnings)
                SELECT @id,@container,@source,@link,@run,@at,@request,@final,@status,'static',s.posture,
                    @raw,@hash,CASE WHEN r.raw_ttl=interval '0' THEN NULL ELSE @body END,@bytes,@type,s.retention_class,
                    CASE WHEN r.raw_ttl IS NULL THEN NULL ELSE @at+r.raw_ttl END,CAST(@warnings AS jsonb)
                FROM harvest_sources s JOIN harvest_retention_policies r ON r.key=s.retention_class WHERE s.id=@source
                """, cancellationToken, ("id", captureId), ("container", containerId), ("source", sourceId), ("link", link.Id),
                ("run", runId), ("at", capture.CapturedAtUtc), ("request", capture.RequestUrl), ("final", capture.FinalUrl),
                ("status", capture.HttpStatus is { } status ? (short)status : null), ("raw", SHA256.HashData(capture.Body.Span)),
                ("hash", contentHash), ("body", capture.Body.ToArray()), ("bytes", capture.Body.Length), ("type", capture.ContentType),
                ("warnings", StoreJson.Write(capture.Warnings)));
            await PostSql.ExecuteAsync(connection, transaction, """
                UPDATE harvest_post_links SET latest_capture_id=@capture,latest_captured_at=@at,latest_content_hash=@hash,url=@url,url_key=@urlKey,
                    origin_key=@origin,locale=@locale,last_changed_at=@now WHERE id=@link;
                INSERT INTO harvest_link_readings(link_id,container_id,capture_id,captured_at,extractor_version,locale,title,body,fields,tier_map,contacts,updated_at)
                VALUES(@link,@container,@capture,@at,@version,@locale,@title,@text,CAST(@fields AS jsonb),CAST(@tiers AS jsonb),CAST(@contacts AS jsonb),@now)
                ON CONFLICT(link_id) DO UPDATE SET capture_id=EXCLUDED.capture_id,captured_at=EXCLUDED.captured_at,
                    extractor_version=EXCLUDED.extractor_version,locale=EXCLUDED.locale,title=EXCLUDED.title,body=EXCLUDED.body,
                    fields=EXCLUDED.fields,tier_map=EXCLUDED.tier_map,contacts=EXCLUDED.contacts,updated_at=EXCLUDED.updated_at
                """, cancellationToken, ("capture", captureId), ("at", capture.CapturedAtUtc), ("hash", contentHash),
                ("url", capture.Url), ("urlKey", MD5.HashData(Encoding.UTF8.GetBytes(NormalizeUrl(capture.Url)))), ("origin", capture.OriginKey), ("locale", capture.Locale), ("now", now), ("link", link.Id),
                ("container", containerId), ("version", envelope.Extraction.RulesVersion), ("title", envelope.Title), ("text", envelope.Text),
                ("fields", StoreJson.Write(envelope)), ("tiers", StoreJson.Write(envelope.FieldSources)), ("contacts", StoreJson.Write(envelope.Contacts)));
        }
        await PostSql.ExecuteAsync(connection, transaction, """
            UPDATE harvest_link_schedule SET last_seen_at=GREATEST(last_seen_at,@at),
                consecutive_missing=0,fail_count=0,next_check_at=GREATEST(next_check_at,@next),due=true WHERE link_id=@id
            """, cancellationToken, ("at", capture.CapturedAtUtc), ("next", capture.CapturedAtUtc.AddHours(6)), ("id", link.Id));
        if (restored)
            await SetLinkStateAsync(connection, transaction, containerId, link, LinkState.Relisted, "seen_again", runId, now, cancellationToken);

        if (changed || restored || newLink)
        {
            await ResolveAsync(connection, transaction, containerId, link.PostId, now, !isNewPost, cancellationToken);
            var post = (await ReadPostAsync(connection, transaction, containerId, link.PostId, cancellationToken))!;
            await DeliveryEventWriter.AppendAsync(connection, transaction, post, isNewPost ? "post.new" : "post.changed", runId, link.Id, now, postFilters, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<CaptureSaveResult>.Ok(new CaptureSaveResult { Post = post, LinkId = link.Id, CaptureId = captureId, Changed = changed });
        }

        var unchanged = (await ReadPostAsync(connection, transaction, containerId, link.PostId, cancellationToken))!;
        await transaction.CommitAsync(cancellationToken);
        return Result<CaptureSaveResult>.Ok(new CaptureSaveResult { Post = unchanged, LinkId = link.Id, CaptureId = null, Changed = false });
    }

    private DateTimeOffset Now()
    {
        var now = time.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
    }

    private static bool IsWebUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);
    private static string NormalizeUrl(string value) => new UriBuilder(value) { Fragment = string.Empty }.Uri.AbsoluteUri;
    private static string Kind(PostEnvelope value) => value.Kind?.ToString().ToLowerInvariant() ?? "unknown";
    private static Result<T> Invalid<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.Validation(message));
    private static Result<T> Missing<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.NotFound(message));
    private static Result<T> Conflict<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.Conflict(message));

    private static async Task<LinkRow?> FindLinkAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId, Guid sourceId, string externalKey, CancellationToken ct)
    {
        await using var command = PostSql.Command(connection, transaction,
            "SELECT id,post_id,latest_content_hash,state,latest_captured_at,state_since FROM harvest_post_links WHERE container_id=@container AND source_id=@source AND external_key=@key",
            ("container", containerId), ("source", sourceId), ("key", externalKey));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new LinkRow(reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetFieldValue<byte[]>(2), Enum.Parse<LinkState>(reader.GetString(3), true),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4), reader.GetFieldValue<DateTimeOffset>(5))
            : null;
    }

    private sealed record LinkRow(Guid Id, Guid PostId, byte[]? Hash, LinkState State, DateTimeOffset? CapturedAtUtc, DateTimeOffset StateSinceUtc);
}
