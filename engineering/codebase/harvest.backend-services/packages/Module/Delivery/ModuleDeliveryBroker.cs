using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Integrates pull subscriptions with a durable transaction-horizon cursor.</summary>
internal sealed class ModuleDeliveryBroker(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource,
    TimeProvider time) : IDeliveryBroker
{
    /// <inheritdoc />
    public async Task<Result<SubscriptionModel>> SubscribeAsync(SubscriptionDeclaration declaration, CancellationToken cancellationToken = default)
    {
        if (declaration?.Context is null) return Invalid<SubscriptionModel>("A subscription and its routing context are required.");
        if (!Enum.IsDefined(declaration.Backfill) || (declaration.Backfill == BackfillMode.Since) != declaration.BackfillSinceUtc.HasValue)
            return Invalid<SubscriptionModel>("A since-backfill requires its lower bound; other modes do not accept one.");
        if (declaration.BackfillSinceUtc > time.GetUtcNow())
            return Invalid<SubscriptionModel>("The backfill lower bound cannot be in the future.");
        if (declaration.Context.Count > 64 || declaration.Context.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 256 || pair.Value is null || pair.Value.Length > 4096))
            return Invalid<SubscriptionModel>("Subscription context exceeds its size limits.");
        if (declaration.Schema is { Length: > 200 }) return Invalid<SubscriptionModel>("The schema identifier is too long.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostSql.LockAsync(connection, transaction, declaration.ContainerId, cancellationToken);
        if (await PostSql.ScalarAsync(connection, transaction,
                "SELECT 1 FROM harvest_tenants WHERE id=@id AND state='active'", cancellationToken, ("id", declaration.TenantId)) is null)
            return Missing<SubscriptionModel>("Active tenant not found.");
        if (await PostSql.ScalarAsync(connection, transaction, """
                SELECT 1 FROM harvest_containers c JOIN harvest_pipelines p ON p.container_id=c.id
                WHERE c.id=@container AND p.id=@pipeline AND (c.kind='pool' OR c.tenant_id=@tenant)
                """, cancellationToken, ("container", declaration.ContainerId), ("pipeline", declaration.PipelineId), ("tenant", declaration.TenantId)) is null)
            return Missing<SubscriptionModel>("Pipeline not found in a container accessible to the tenant.");
        var now = time.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % 10));
        var id = Guid.CreateVersion7(now);
        await PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_subscriptions(id,tenant_id,container_id,pipeline_id,kind,mode,context,schema,backfill,backfill_since,created_at)
            VALUES(@id,@tenant,@container,@pipeline,'channel','pull',CAST(@context AS jsonb),@schema,@backfill,@since,@now)
            """, cancellationToken, ("id", id), ("tenant", declaration.TenantId), ("container", declaration.ContainerId),
            ("pipeline", declaration.PipelineId), ("context", StoreJson.Write(declaration.Context)), ("schema", declaration.Schema),
            ("backfill", declaration.Backfill.ToString().ToLowerInvariant()), ("since", declaration.BackfillSinceUtc?.ToUniversalTime()), ("now", now));
        if (declaration.Backfill != BackfillMode.None)
        {
            var posts = new List<Guid>();
            await using (var command = PostSql.Command(connection, transaction, """
                             SELECT DISTINCT p.id FROM harvest_posts p JOIN harvest_post_links l ON l.post_id=p.id
                             JOIN harvest_pipelines q ON q.source_id=l.source_id
                             WHERE q.id=@pipeline AND p.container_id=@container AND p.merged_into IS NULL AND p.state='live'
                               AND l.retired_at IS NULL AND (CAST(@since AS timestamptz) IS NULL OR p.first_seen_at>=@since)
                             ORDER BY p.id
                             """, ("pipeline", declaration.PipelineId), ("container", declaration.ContainerId), ("since", declaration.BackfillSinceUtc?.ToUniversalTime())))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken)) posts.Add(reader.GetGuid(0));
            foreach (var postId in posts)
            {
                var post = (await ModulePostBroker.ReadPostAsync(connection, transaction, declaration.ContainerId, postId, cancellationToken))!;
                await DeliveryEventWriter.AppendOneAsync(connection, transaction, id, post, DeliveryEventTypes.PostNew, null, null, "backfill", now, cancellationToken);
            }
        }
        var model = (await ReadSubscriptionAsync(connection, transaction, id, cancellationToken))!;
        await transaction.CommitAsync(cancellationToken);
        return Result<SubscriptionModel>.Ok(model);
    }

    /// <inheritdoc />
    public async Task<Result<SubscriptionModel>> GetSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var subscription = await ReadSubscriptionAsync(connection, null, subscriptionId, cancellationToken);
        return subscription is null ? Missing<SubscriptionModel>("Subscription not found.") : Result<SubscriptionModel>.Ok(subscription);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SubscriptionModel>>> ListSubscriptionsAsync(Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var ids = new List<Guid>();
        await using (var command = PostSql.Command(connection, null,
                         "SELECT id FROM harvest_subscriptions WHERE CAST(@tenant AS uuid) IS NULL OR tenant_id=@tenant ORDER BY created_at,id", ("tenant", tenantId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
        var subscriptions = new List<SubscriptionModel>();
        foreach (var id in ids) subscriptions.Add((await ReadSubscriptionAsync(connection, null, id, cancellationToken))!);
        return Result<IReadOnlyList<SubscriptionModel>>.Ok(subscriptions);
    }

    /// <inheritdoc />
    public async Task<Result<CursorPage<DeliveryEvent>>> PullAsync(Guid subscriptionId, string? cursor = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500) return Invalid<CursorPage<DeliveryEvent>>("The limit must be between 1 and 500.");
        if (!TryCursor(subscriptionId, cursor, out var transactionId, out var rowId))
            return Invalid<CursorPage<DeliveryEvent>>("The cursor is malformed or belongs to a different subscription.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var subscription = await ReadSubscriptionAsync(connection, null, subscriptionId, cancellationToken);
        if (subscription is null) return Missing<CursorPage<DeliveryEvent>>("Subscription not found.");
        var rows = new List<(ulong TransactionId, long Id, DeliveryEvent Event)>();
        await using (var command = PostSql.Command(connection, null, """
                         SELECT tx::text,id,event_id,item_id,type,occurred_at,post_id,revision,link_id,run_id,reason,payload::text
                         FROM harvest_subscription_event WHERE subscription_id=@subscription
                           AND tx < pg_snapshot_xmin(pg_current_snapshot())
                           AND (tx,id) > (CAST(@tx AS xid8),@row)
                         ORDER BY tx,id LIMIT @limit
                         """, ("subscription", subscriptionId), ("tx", transactionId.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("row", rowId), ("limit", limit + 1)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                rows.Add((ulong.Parse(reader.GetString(0), System.Globalization.CultureInfo.InvariantCulture), reader.GetInt64(1), new DeliveryEvent
                {
                    EventId = reader.GetGuid(2), ItemId = reader.GetString(3), SubscriptionId = subscriptionId, Type = reader.GetString(4),
                    OccurredAtUtc = reader.GetFieldValue<DateTimeOffset>(5), PostId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
                    Revision = reader.IsDBNull(7) ? null : reader.GetInt32(7), LinkId = reader.IsDBNull(8) ? null : reader.GetGuid(8),
                    RunId = reader.IsDBNull(9) ? null : reader.GetGuid(9), Reason = reader.IsDBNull(10) ? null : reader.GetString(10),
                    Data = reader.IsDBNull(11) ? null : RenderPayload(JsonSerializer.Deserialize<JsonElement>(reader.GetString(11)), subscription.Schema), Context = subscription.Context,
                }));
        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var next = rows.Count == 0 ? cursor ?? EncodeCursor(subscriptionId, transactionId, rowId)
            : EncodeCursor(subscriptionId, rows[^1].TransactionId, rows[^1].Id);
        return Result<CursorPage<DeliveryEvent>>.Ok(new CursorPage<DeliveryEvent> { Data = rows.Select(row => row.Event).ToArray(), Next = next, HasMore = hasMore });
    }

    private static async Task<SubscriptionModel?> ReadSubscriptionAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid id, CancellationToken ct)
    {
        await using var command = PostSql.Command(connection, transaction, """
            SELECT id,tenant_id,pipeline_id,context::text,schema,backfill,state,created_at
            FROM harvest_subscriptions WHERE id=@id
            """, ("id", id));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return !await reader.ReadAsync(ct) ? null : new SubscriptionModel
        {
            Id = reader.GetGuid(0), TenantId = reader.GetGuid(1), PipelineId = reader.GetGuid(2), Kind = SubscriptionKind.Channel,
            Mode = DeliveryMode.Pull, Context = StoreJson.Read(reader.GetString(3), new Dictionary<string, string>()),
            Schema = reader.IsDBNull(4) ? null : reader.GetString(4), Backfill = Enum.Parse<BackfillMode>(reader.GetString(5), true),
            State = Enum.Parse<SubscriptionState>(reader.GetString(6), true), CreatedAtUtc = reader.GetFieldValue<DateTimeOffset>(7),
        };
    }

    private static string EncodeCursor(Guid subscriptionId, ulong transactionId, long rowId) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{subscriptionId:N}:{transactionId}:{rowId}"));

    private static bool TryCursor(Guid subscriptionId, string? cursor, out ulong transactionId, out long rowId)
    {
        transactionId = 0; rowId = 0;
        if (string.IsNullOrEmpty(cursor)) return true;
        if (cursor.Length > 256) return false;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(':');
            return parts.Length == 3 && Guid.TryParseExact(parts[0], "N", out var owner) && owner == subscriptionId
                && ulong.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out transactionId)
                && long.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out rowId) && rowId >= 0;
        }
        catch (FormatException) { return false; }
    }

    private static JsonElement RenderPayload(JsonElement payload, string? schema)
    {
        if (schema != "wow2.event.v1" || !payload.TryGetProperty("base", out var envelope)
            || !envelope.TryGetProperty("extras", out var extras) || !extras.TryGetProperty("event", out var mapped)
            || mapped.ValueKind != JsonValueKind.Object) return payload;
        var values = mapped.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
        foreach (var (source, target) in new[] { ("id", "postId"), ("revision", "revision"), ("state", "state"), ("links", "links"), ("fieldSources", "fieldSources"), ("mergedInto", "mergedInto") })
            if (payload.TryGetProperty(source, out var value)) values[target] = value.Clone();
        return JsonSerializer.SerializeToElement(values);
    }

    private static Result<T> Invalid<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.Validation(message));
    private static Result<T> Missing<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.NotFound(message));
}
