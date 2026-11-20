using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Integrates pull subscriptions with a durable transaction-horizon cursor, for channels over one pipeline and
/// for selectors over Layer 1 tags and sources.</summary>
internal sealed class ModuleDeliveryBroker(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource,
    TimeProvider time,
    IEnumerable<IDeliveryPostFilter> filters,
    IEnumerable<ISelectorChangeHook> hooks,
    ILogger<ModuleDeliveryBroker> logger) : IDeliveryBroker
{
    private readonly DeliveryPostFilters postFilters = new(filters);

    /// <inheritdoc />
    public async Task<Result<SubscriptionModel>> SubscribeAsync(SubscriptionDeclaration declaration, CancellationToken cancellationToken = default)
    {
        if (declaration?.Context is null) return Invalid<SubscriptionModel>("A subscription and its routing context are required.");
        if (declaration.Selector is not null) return await SubscribeSelectorAsync(declaration, cancellationToken);
        if (declaration.PipelineId is null) return Invalid<SubscriptionModel>("A channel subscription needs its pipeline.");
        if (declaration.Name is not null) return Invalid<SubscriptionModel>("Only a selector subscription has a name.");
        if (CommonProblem(declaration) is { } problem) return Invalid<SubscriptionModel>(problem);
        if (declaration.FilterKey is { } filterKey && (string.IsNullOrWhiteSpace(filterKey) || filterKey.Length > 200 || !postFilters.Contains(filterKey)))
            return Invalid<SubscriptionModel>("The requested post filter is not registered or its key is invalid.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostSql.LockAsync(connection, transaction, declaration.ContainerId, cancellationToken);
        if (await PostSql.ScalarAsync(connection, transaction,
                "SELECT 1 FROM harvest_tenants WHERE id=@id AND state='active'", cancellationToken, ("id", declaration.TenantId)) is null)
            return Missing<SubscriptionModel>("Active tenant not found.");
        if (await PostSql.ScalarAsync(connection, transaction, """
                SELECT p.scope::text FROM harvest_containers c JOIN harvest_pipelines p ON p.container_id=c.id
                WHERE c.id=@container AND p.id=@pipeline AND (c.kind='pool' OR c.tenant_id=@tenant)
                """, cancellationToken, ("container", declaration.ContainerId), ("pipeline", declaration.PipelineId), ("tenant", declaration.TenantId)) is not string scopeJson)
            return Missing<SubscriptionModel>("Pipeline not found in a container accessible to the tenant.");
        var now = time.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % 10));
        var id = Guid.CreateVersion7(now);
        await PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_subscriptions(id,tenant_id,container_id,pipeline_id,kind,mode,context,schema,filter_key,backfill,backfill_since,created_at)
            VALUES(@id,@tenant,@container,@pipeline,'channel','pull',CAST(@context AS jsonb),@schema,@filter,@backfill,@since,@now)
            """, cancellationToken, ("id", id), ("tenant", declaration.TenantId), ("container", declaration.ContainerId),
            ("pipeline", declaration.PipelineId), ("context", StoreJson.Write(declaration.Context)), ("schema", declaration.Schema),
            ("filter", declaration.FilterKey),
            ("backfill", declaration.Backfill.ToString().ToLowerInvariant()), ("since", declaration.BackfillSinceUtc?.ToUniversalTime()), ("now", now));
        if (declaration.Backfill != BackfillMode.None)
        {
            var scope = StoreJson.Read(scopeJson, new PipelineScope());
            var posts = new SortedSet<Guid>();
            await using (var command = PostSql.Command(connection, transaction, """
                             SELECT DISTINCT p.id,l.url FROM harvest_posts p JOIN harvest_post_links l ON l.post_id=p.id
                             JOIN harvest_pipelines q ON q.source_id=l.source_id
                             WHERE q.id=@pipeline AND p.container_id=@container AND p.merged_into IS NULL AND p.state='live'
                               AND l.retired_at IS NULL AND (CAST(@since AS timestamptz) IS NULL OR p.first_seen_at>=@since)
                             ORDER BY p.id
                             """, ("pipeline", declaration.PipelineId), ("container", declaration.ContainerId), ("since", declaration.BackfillSinceUtc?.ToUniversalTime())))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken))
                    if (scope.IncludesUrl(reader.GetString(1)))
                        posts.Add(reader.GetGuid(0));
            foreach (var postId in posts)
            {
                var post = (await ModulePostBroker.ReadPostAsync(connection, transaction, declaration.ContainerId, postId, cancellationToken))!;
                if (postFilters.Matches(declaration.FilterKey, post.Base))
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
                         SELECT e.tx::text,e.id,e.event_id,e.item_id,e.type,e.occurred_at,e.post_id,e.revision,e.link_id,e.run_id,e.reason,e.payload::text,e.matched::text
                         FROM harvest_subscription_event e WHERE e.subscription_id=@subscription
                           AND e.tx < pg_snapshot_xmin(pg_current_snapshot())
                           AND (e.tx,e.id) > (CAST(@tx AS xid8),@row)
                         ORDER BY e.tx,e.id LIMIT @limit
                         """, ("subscription", subscriptionId), ("tx", transactionId.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("row", rowId), ("limit", limit + 1)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                var matched = reader.IsDBNull(12) ? null : JsonSerializer.Deserialize<StoredMatch>(reader.GetString(12), JsonOptionsConstants.Default);
                var data = reader.IsDBNull(11) ? (JsonElement?)null : RenderPayload(JsonSerializer.Deserialize<JsonElement>(reader.GetString(11)), subscription.Schema);
                if (data is { } payload && matched?.Context is { } matchedContext)
                    data = WithContext(payload, matchedContext);
                rows.Add((ulong.Parse(reader.GetString(0), System.Globalization.CultureInfo.InvariantCulture), reader.GetInt64(1), new DeliveryEvent
                {
                    EventId = reader.GetGuid(2), ItemId = reader.GetString(3), SubscriptionId = subscriptionId, PipelineId = subscription.PipelineId, Type = reader.GetString(4),
                    OccurredAtUtc = reader.GetFieldValue<DateTimeOffset>(5), PostId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
                    Revision = reader.IsDBNull(7) ? null : reader.GetInt32(7), LinkId = reader.IsDBNull(8) ? null : reader.GetGuid(8),
                    RunId = reader.IsDBNull(9) ? null : reader.GetGuid(9), Reason = reader.IsDBNull(10) ? null : reader.GetString(10),
                    Data = data, Context = subscription.Context,
                    Match = matched is null ? null : new SubscriptionMatch { SelectorVersion = matched.SelectorVersion, Verdict = matched.Verdict, Clauses = matched.Clauses },
                }));
            }
        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        if (subscription.Schema == PostEnvelopeConstants.EventSchema
            && await DeliveryMediaFiles.ReadAsync(connection, subscriptionId, rows.Select(row => row.Event), cancellationToken) is { Count: > 0 } copies)
            for (var index = 0; index < rows.Count; index++)
                if (rows[index].Event is { PostId: { } postId, Data: { } payload } delivered && DeliveryMediaFiles.Apply(payload, postId, copies) is { } filled)
                    rows[index] = (rows[index].TransactionId, rows[index].Id, delivered with { Data = filled });
        var next = rows.Count == 0 ? cursor ?? EncodeCursor(subscriptionId, transactionId, rowId)
            : EncodeCursor(subscriptionId, rows[^1].TransactionId, rows[^1].Id);
        return Result<CursorPage<DeliveryEvent>>.Ok(new CursorPage<DeliveryEvent> { Data = rows.Select(row => row.Event).ToArray(), Next = next, HasMore = hasMore });
    }

    /// <inheritdoc />
    public async Task<Result<SelectorChangeModel>> SetSelectorAsync(Guid subscriptionId, SelectorChange change, CancellationToken cancellationToken = default)
    {
        if (change?.Selector is null) return Invalid<SelectorChangeModel>("A selector change needs its selector.");
        if (change.ChangedBy is { Length: > 200 }) return Invalid<SelectorChangeModel>("The changer's name is too long.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await PostSql.ScalarAsync(connection, transaction, "SELECT container_id FROM harvest_subscriptions WHERE id=@id", cancellationToken, ("id", subscriptionId)) is not Guid containerId)
            return Missing<SelectorChangeModel>("Subscription not found.");
        await PostSql.LockAsync(connection, transaction, containerId, cancellationToken);
        string kind, state;
        string? schema;
        IReadOnlyDictionary<string, string> context;
        int? storedVersion;
        await using (var command = PostSql.Command(connection, transaction,
                         "SELECT kind,state,schema,context::text,selector_version FROM harvest_subscriptions WHERE id=@id", ("id", subscriptionId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return Missing<SelectorChangeModel>("Subscription not found.");
            kind = reader.GetString(0); state = reader.GetString(1); schema = reader.IsDBNull(2) ? null : reader.GetString(2);
            context = StoreJson.Read(reader.GetString(3), new Dictionary<string, string>());
            storedVersion = reader.IsDBNull(4) ? null : reader.GetInt32(4);
        }
        if (kind != "selector" || storedVersion is not { } version) return Invalid<SelectorChangeModel>("Only a selector subscription has a selector.");
        if (state == "archived") return Conflict<SelectorChangeModel>("An archived subscription cannot change its selector.");
        var selector = SelectorRules.Normalize(change.Selector);
        if (await SelectorStore.ValidateAsync(connection, transaction, containerId, selector, cancellationToken) is { } problem)
            return Invalid<SelectorChangeModel>(problem);
        var now = Now();
        var window = change.WindowSinceUtc?.ToUniversalTime() ?? now.AddDays(-SelectorRules.DefaultWindowDays);
        if (window > now) return Invalid<SelectorChangeModel>("The window's lower bound cannot be in the future.");
        if (window < now.AddDays(-SelectorRules.MaxWindowDays))
            return Invalid<SelectorChangeModel>($"The window cannot reach back more than {SelectorRules.MaxWindowDays} days.");
        var json = StoreJson.Write(selector);
        if (await PostSql.ScalarAsync(connection, transaction, "SELECT selector = CAST(@selector AS jsonb) FROM harvest_subscriptions WHERE id=@id",
                cancellationToken, ("id", subscriptionId), ("selector", json)) is true)
            return Result<SelectorChangeModel>.Ok(new SelectorChangeModel { Subscription = (await ReadSubscriptionAsync(connection, transaction, subscriptionId, cancellationToken))!, Changed = false });

        var candidates = await SelectorStore.ReadWindowAsync(connection, transaction, containerId, subscriptionId, window, SelectorRules.MaxWindowPosts + 1, cancellationToken);
        if (candidates.Count > SelectorRules.MaxWindowPosts)
            return Invalid<SelectorChangeModel>($"The window holds more than {SelectorRules.MaxWindowPosts} posts; narrow it.");
        var next = version + 1;
        await PostSql.ExecuteAsync(connection, transaction, "UPDATE harvest_subscriptions SET selector=CAST(@selector AS jsonb),selector_version=@version WHERE id=@id",
            cancellationToken, ("id", subscriptionId), ("selector", json), ("version", next));
        var scope = await SelectorStore.ReadScopeAsync(connection, transaction, containerId, [selector], cancellationToken);
        string suffix = $":s{next}";
        int considered = 0, newMatches = 0, unmatched = 0, rematched = 0, pending = 0;
        foreach (var candidate in candidates)
        {
            if (await ModulePostBroker.ReadPostAsync(connection, transaction, containerId, candidate.PostId, cancellationToken) is not { } post) continue;
            considered++;
            var evaluation = SubscriptionSelectorMatcher.Evaluate(selector, SelectorStore.Facts(post, scope.Groups), scope.Sections, schema, context);
            if (evaluation.Verdict == SelectorVerdict.Pending) { pending++; continue; }
            var (type, reason) = !candidate.Seen ? (evaluation.Verdict == SelectorVerdict.Match ? DeliveryEventTypes.PostNew : null, "backfill")
                : !candidate.Unmatched ? (evaluation.Verdict == SelectorVerdict.NoMatch ? DeliveryEventTypes.PostUnmatched : null, "resync")
                : (evaluation.Verdict == SelectorVerdict.Match ? DeliveryEventTypes.PostChanged : null, "resync");
            if (type is null) continue;
            if (await DeliveryEventWriter.AppendOneAsync(connection, transaction, subscriptionId, post, type, null, null, reason, now, cancellationToken,
                    SelectorStore.Matched(next, evaluation), suffix) == 0) continue;
            if (type == DeliveryEventTypes.PostNew) newMatches++;
            else if (type == DeliveryEventTypes.PostUnmatched) unmatched++;
            else rematched++;
        }
        await InsertSelectorVersionAsync(connection, transaction, subscriptionId, next, json, window, (considered, newMatches, unmatched, rematched, pending), change.ChangedBy, now, cancellationToken);
        var model = (await ReadSubscriptionAsync(connection, transaction, subscriptionId, cancellationToken))!;
        await transaction.CommitAsync(cancellationToken);
        await RunHooksAsync(subscriptionId, window, cancellationToken);
        return Result<SelectorChangeModel>.Ok(new SelectorChangeModel
        {
            Subscription = model, Changed = true, WindowSinceUtc = window, Considered = considered, NewMatches = newMatches,
            Unmatched = unmatched, Rematched = rematched, Pending = pending,
        });
    }

    /// <inheritdoc />
    public async Task<Result<SubscriptionModel>> SetStateAsync(Guid subscriptionId, SubscriptionState state, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(state)) return Invalid<SubscriptionModel>("The subscription state is not one of active, suspended or archived.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await PostSql.ScalarAsync(connection, transaction, "SELECT container_id FROM harvest_subscriptions WHERE id=@id", cancellationToken, ("id", subscriptionId)) is not Guid containerId)
            return Missing<SubscriptionModel>("Subscription not found.");
        await PostSql.LockAsync(connection, transaction, containerId, cancellationToken);
        if (await PostSql.ScalarAsync(connection, transaction, "SELECT state FROM harvest_subscriptions WHERE id=@id", cancellationToken, ("id", subscriptionId)) is not string current)
            return Missing<SubscriptionModel>("Subscription not found.");
        var wanted = state.ToString().ToLowerInvariant();
        if (current != wanted)
        {
            if (current == "archived") return Conflict<SubscriptionModel>("An archived subscription stays archived.");
            await PostSql.ExecuteAsync(connection, transaction, "UPDATE harvest_subscriptions SET state=@state WHERE id=@id", cancellationToken,
                ("id", subscriptionId), ("state", wanted));
        }
        var model = (await ReadSubscriptionAsync(connection, transaction, subscriptionId, cancellationToken))!;
        await transaction.CommitAsync(cancellationToken);
        return Result<SubscriptionModel>.Ok(model);
    }

    /// <summary>Subscribes a tenant to a selector over one container and backfills the live posts it matches.</summary>
    private async Task<Result<SubscriptionModel>> SubscribeSelectorAsync(SubscriptionDeclaration declaration, CancellationToken cancellationToken)
    {
        if (declaration.PipelineId is not null) return Invalid<SubscriptionModel>("A selector subscription follows no pipeline; its sources are in the selector.");
        if (declaration.FilterKey is not null) return Invalid<SubscriptionModel>("A selector subscription takes no filter key; its selector is the filter.");
        if (!SelectorRules.IsName(declaration.Name))
            return Invalid<SubscriptionModel>("A selector subscription needs a name: a lowercase letter or digit, then up to 63 letters, digits or hyphens.");
        if (CommonProblem(declaration) is { } common) return Invalid<SubscriptionModel>(common);
        var selector = SelectorRules.Normalize(declaration.Selector!);
        if (SelectorRules.Validate(selector) is { } shape) return Invalid<SubscriptionModel>(shape);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostSql.LockAsync(connection, transaction, declaration.ContainerId, cancellationToken);
        if (await PostSql.ScalarAsync(connection, transaction,
                "SELECT 1 FROM harvest_tenants WHERE id=@id AND state='active'", cancellationToken, ("id", declaration.TenantId)) is null)
            return Missing<SubscriptionModel>("Active tenant not found.");
        if (await PostSql.ScalarAsync(connection, transaction, "SELECT 1 FROM harvest_containers WHERE id=@container AND (kind='pool' OR tenant_id=@tenant)",
                cancellationToken, ("container", declaration.ContainerId), ("tenant", declaration.TenantId)) is null)
            return Missing<SubscriptionModel>("Container not found or not accessible to the tenant.");
        if (await PostSql.ScalarAsync(connection, transaction, "SELECT 1 FROM harvest_subscriptions WHERE tenant_id=@tenant AND name=@name AND state<>'archived'",
                cancellationToken, ("tenant", declaration.TenantId), ("name", declaration.Name)) is not null)
            return Conflict<SubscriptionModel>($"The tenant already has a subscription named '{declaration.Name}'.");
        if (await SelectorStore.ValidateAsync(connection, transaction, declaration.ContainerId, selector, cancellationToken) is { } problem)
            return Invalid<SubscriptionModel>(problem);

        var now = Now();
        var id = Guid.CreateVersion7(now);
        var since = declaration.Backfill == BackfillMode.Since ? declaration.BackfillSinceUtc?.ToUniversalTime() : null;
        var json = StoreJson.Write(selector);
        try
        {
            await PostSql.ExecuteAsync(connection, transaction, """
                INSERT INTO harvest_subscriptions(id,tenant_id,container_id,pipeline_id,kind,mode,context,schema,backfill,backfill_since,name,selector,selector_version,created_at)
                VALUES(@id,@tenant,@container,NULL,'selector','pull',CAST(@context AS jsonb),@schema,@backfill,@since,@name,CAST(@selector AS jsonb),1,@now)
                """, cancellationToken, ("id", id), ("tenant", declaration.TenantId), ("container", declaration.ContainerId),
                ("context", StoreJson.Write(declaration.Context)), ("schema", declaration.Schema),
                ("backfill", declaration.Backfill.ToString().ToLowerInvariant()), ("since", since), ("name", declaration.Name), ("selector", json), ("now", now));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict<SubscriptionModel>($"The tenant already has a subscription named '{declaration.Name}'.");
        }

        int considered = 0, newMatches = 0, pending = 0;
        if (declaration.Backfill != BackfillMode.None)
        {
            var scope = await SelectorStore.ReadScopeAsync(connection, transaction, declaration.ContainerId, [selector], cancellationToken);
            var posts = new List<Guid>();
            await using (var command = PostSql.Command(connection, transaction, """
                             SELECT p.id FROM harvest_posts p
                             WHERE p.container_id=@container AND p.merged_into IS NULL AND p.purged_at IS NULL AND p.state='live'
                               AND (CAST(@since AS timestamptz) IS NULL OR p.first_seen_at>=@since)
                             ORDER BY p.first_seen_at,p.id
                             """, ("container", declaration.ContainerId), ("since", since)))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken)) posts.Add(reader.GetGuid(0));
            foreach (var postId in posts)
            {
                if (await ModulePostBroker.ReadPostAsync(connection, transaction, declaration.ContainerId, postId, cancellationToken) is not { } post) continue;
                considered++;
                var evaluation = SubscriptionSelectorMatcher.Evaluate(selector, SelectorStore.Facts(post, scope.Groups), scope.Sections, declaration.Schema, declaration.Context);
                if (evaluation.Verdict == SelectorVerdict.Pending) pending++;
                else if (evaluation.Verdict == SelectorVerdict.Match
                         && await DeliveryEventWriter.AppendOneAsync(connection, transaction, id, post, DeliveryEventTypes.PostNew, null, null, "backfill", now,
                             cancellationToken, SelectorStore.Matched(1, evaluation)) > 0)
                    newMatches++;
            }
        }
        await InsertSelectorVersionAsync(connection, transaction, id, 1, json, since, (considered, newMatches, 0, 0, pending), null, now, cancellationToken);
        var model = (await ReadSubscriptionAsync(connection, transaction, id, cancellationToken))!;
        await transaction.CommitAsync(cancellationToken);
        await RunHooksAsync(id, since, cancellationToken);
        return Result<SubscriptionModel>.Ok(model);
    }

    /// <summary>Records one selector version in the subscription's history, with the window it re-judged and what that wrote.</summary>
    private static Task<int> InsertSelectorVersionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid subscriptionId, int version, string selector,
        DateTimeOffset? window, (int Considered, int NewMatches, int Unmatched, int Rematched, int Pending) counts, string? changedBy, DateTimeOffset now, CancellationToken ct) =>
        PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_subscription_selectors(subscription_id,version,selector,window_since,counts,changed_by,created_at)
            VALUES(@subscription,@version,CAST(@selector AS jsonb),@window,CAST(@counts AS jsonb),@by,@now)
            """, ct, ("subscription", subscriptionId), ("version", version), ("selector", selector), ("window", window), ("by", changedBy), ("now", now),
            ("counts", StoreJson.Write(new { considered = counts.Considered, newMatches = counts.NewMatches, unmatched = counts.Unmatched, rematched = counts.Rematched, pending = counts.Pending })));

    /// <summary>Calls each selector-change hook after the change committed; a hook that fails is logged and never fails the write.</summary>
    private async Task RunHooksAsync(Guid subscriptionId, DateTimeOffset? windowSinceUtc, CancellationToken cancellationToken)
    {
        foreach (var hook in hooks)
        {
            try
            {
                await hook.OnChangedAsync(subscriptionId, windowSinceUtc, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Selector change hook {Hook} failed for subscription {SubscriptionId}", hook.GetType().Name, subscriptionId);
            }
        }
    }

    /// <summary>Returns the first problem in what every declaration shares: its backfill, routing context and schema.</summary>
    private string? CommonProblem(SubscriptionDeclaration declaration)
    {
        if (!Enum.IsDefined(declaration.Backfill) || (declaration.Backfill == BackfillMode.Since) != declaration.BackfillSinceUtc.HasValue)
            return "A since-backfill requires its lower bound; other modes do not accept one.";
        if (declaration.BackfillSinceUtc > time.GetUtcNow())
            return "The backfill lower bound cannot be in the future.";
        if (declaration.Context.Count > 64 || declaration.Context.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 256 || pair.Value is null || pair.Value.Length > 4096))
            return "Subscription context exceeds its size limits.";
        return declaration.Schema is { Length: > 200 } ? "The schema identifier is too long." : null;
    }

    /// <summary>Returns the clock's now, cut to the microsecond PostgreSQL keeps.</summary>
    private DateTimeOffset Now()
    {
        var now = time.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
    }

    private static async Task<SubscriptionModel?> ReadSubscriptionAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid id, CancellationToken ct)
    {
        await using var command = PostSql.Command(connection, transaction, """
            SELECT id,tenant_id,pipeline_id,context::text,schema,backfill,state,created_at,filter_key,kind,name,selector::text,selector_version
            FROM harvest_subscriptions WHERE id=@id
            """, ("id", id));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return !await reader.ReadAsync(ct) ? null : new SubscriptionModel
        {
            Id = reader.GetGuid(0), TenantId = reader.GetGuid(1), PipelineId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
            Kind = Enum.Parse<SubscriptionKind>(reader.GetString(9), true),
            Mode = DeliveryMode.Pull, Context = StoreJson.Read(reader.GetString(3), new Dictionary<string, string>()),
            Schema = reader.IsDBNull(4) ? null : reader.GetString(4), Backfill = Enum.Parse<BackfillMode>(reader.GetString(5), true),
            State = Enum.Parse<SubscriptionState>(reader.GetString(6), true), CreatedAtUtc = reader.GetFieldValue<DateTimeOffset>(7),
            FilterKey = reader.IsDBNull(8) ? null : reader.GetString(8),
            Name = reader.IsDBNull(10) ? null : reader.GetString(10),
            Selector = reader.IsDBNull(11) ? null : SelectorStore.ReadSelector(reader.GetString(11)),
            SelectorVersion = reader.IsDBNull(12) ? null : reader.GetInt32(12),
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
        if (schema != PostEnvelopeConstants.EventSchema || !payload.TryGetProperty("base", out var envelope)
            || envelope.ValueKind != JsonValueKind.Object) return payload;
        // A stored projection without SCFD's mapping stamp is a reader's own and wins. One with the stamp came from
        // EventSchemaMapper, so the current mapper renders it again from the stored post, on today's wire.
        JsonElement? stored = envelope.TryGetProperty("extras", out var extras) && extras.ValueKind == JsonValueKind.Object
            && extras.TryGetProperty("event", out var storedEvent) && storedEvent.ValueKind == JsonValueKind.Object ? storedEvent : null;
        JsonElement mapped;
        if (stored is { } own && !own.TryGetProperty("mapping", out _))
            mapped = own;
        else if (envelope.Deserialize<PostEnvelope>(JsonOptionsConstants.Default) is { } post && EventSchemaMapper.Map(post) is { } projection)
            mapped = projection;
        else if (stored is { } fallback)
            mapped = fallback;
        else
            return payload;
        var values = mapped.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
        if (values.TryGetValue("links", out var contentLinks)) values["contentLinks"] = contentLinks;
        foreach (var (source, target) in new[] { ("id", "postId"), ("revision", "revision"), ("state", "state"), ("links", "links"), ("fieldSources", "fieldSources"), ("mergedInto", "mergedInto") })
            if (payload.TryGetProperty(source, out var value)) values[target] = value.Clone();
        if (payload.TryGetProperty("links", out var links) && Capture(links) is { } capture) values["capture"] = capture;
        return JsonSerializer.SerializeToElement(values);
    }

    /// <summary>Names the newest capture behind the revision: its id, time and URL, from the post's captured links.</summary>
    private static JsonElement? Capture(JsonElement links)
    {
        if (links.ValueKind != JsonValueKind.Array) return null;
        var newest = links.EnumerateArray()
            .Where(link => link.ValueKind == JsonValueKind.Object
                && link.TryGetProperty("captureId", out var id) && id.ValueKind == JsonValueKind.String
                && link.TryGetProperty("capturedAtUtc", out var at) && at.ValueKind == JsonValueKind.String)
            .OrderByDescending(link => link.GetProperty("capturedAtUtc").GetDateTimeOffset())
            .Cast<JsonElement?>()
            .FirstOrDefault();
        return newest is not { } link ? null : JsonSerializer.SerializeToElement(new
        {
            captureId = link.GetProperty("captureId").GetString(),
            capturedAtUtc = link.GetProperty("capturedAtUtc").GetString(),
            url = link.TryGetProperty("url", out var url) ? url.GetString() : null,
        });
    }

    /// <summary>Returns the payload with its top-level <c>context</c> set to the event's stored context.</summary>
    private static JsonElement WithContext(JsonElement payload, IReadOnlyDictionary<string, string> context)
    {
        if (payload.ValueKind != JsonValueKind.Object) return payload;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in payload.EnumerateObject())
                if (property.Name != "context") property.WriteTo(writer);
            writer.WritePropertyName("context");
            JsonSerializer.SerializeToElement(context, JsonOptionsConstants.Default).WriteTo(writer);
            writer.WriteEndObject();
        }
        return JsonSerializer.Deserialize<JsonElement>(stream.ToArray());
    }

    private static Result<T> Invalid<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.Validation(message));
    private static Result<T> Conflict<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.Conflict(message));
    private static Result<T> Missing<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.NotFound(message));
}
