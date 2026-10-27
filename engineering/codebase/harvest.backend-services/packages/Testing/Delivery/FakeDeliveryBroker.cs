using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;

/// <summary>Implements pull subscriptions over a <see cref="FakePostBroker"/> for consumer tests.</summary>
public sealed class FakeDeliveryBroker : IDeliveryBroker
{
    private readonly FakePostBroker posts;
    private readonly IRegistrationBroker registry;
    private readonly TimeProvider time;
    private readonly Dictionary<string, IDeliveryPostFilter> postFilters;
    private readonly Dictionary<Guid, Entry> subscriptions = [];

    /// <summary>Creates a fake over the same registry and post store used by the consumer's other tests.</summary>
    public FakeDeliveryBroker(FakePostBroker posts, IRegistrationBroker registry, TimeProvider? clock = null)
        : this(posts, registry, clock, null) { }

    /// <summary>Creates a fake with the host's optional subscription filters.</summary>
    public FakeDeliveryBroker(FakePostBroker posts, IRegistrationBroker registry, TimeProvider? clock, IEnumerable<IDeliveryPostFilter>? filters)
    {
        this.posts = posts; this.registry = registry; time = clock ?? TimeProvider.System;
        postFilters = (filters ?? []).ToDictionary(filter => filter.Key, StringComparer.Ordinal);
        posts.Changed += Observe;
    }

    /// <inheritdoc />
    public async Task<Result<SubscriptionModel>> SubscribeAsync(SubscriptionDeclaration declaration, CancellationToken cancellationToken = default)
    {
        if (declaration?.Context is null) return Invalid<SubscriptionModel>("A subscription and its routing context are required.");
        if (!Enum.IsDefined(declaration.Backfill) || (declaration.Backfill == BackfillMode.Since) != declaration.BackfillSinceUtc.HasValue
            || declaration.BackfillSinceUtc > time.GetUtcNow() || declaration.Schema is { Length: > 200 }
            || declaration.Context.Count > 64 || declaration.Context.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 256 || pair.Value is null || pair.Value.Length > 4096))
            return Invalid<SubscriptionModel>("The subscription is malformed.");
        if (declaration.FilterKey is { } filterKey && (string.IsNullOrWhiteSpace(filterKey) || filterKey.Length > 200 || !postFilters.ContainsKey(filterKey)))
            return Invalid<SubscriptionModel>("The requested post filter is not registered or its key is invalid.");
        if (await registry.GetTenantAsync(declaration.TenantId, cancellationToken) is not Result<TenantModel>.Success { Value.State: TenantState.Active })
            return Missing<SubscriptionModel>("Active tenant not found.");
        if (await registry.GetContainerAsync(declaration.ContainerId, cancellationToken) is not Result<ContainerModel>.Success { Value: var container }
            || (container.Kind != ContainerKind.Pool && container.TenantId != declaration.TenantId)
            || await registry.GetPipelineAsync(declaration.PipelineId, cancellationToken) is not Result<PipelineModel>.Success { Value: var pipeline }
            || pipeline.ContainerId != declaration.ContainerId)
            return Missing<SubscriptionModel>("Pipeline not found in an accessible container.");
        lock (posts.Gate)
        {
            var now = time.GetUtcNow();
            var subscription = new SubscriptionModel
            {
                Id = Guid.CreateVersion7(now), TenantId = declaration.TenantId, PipelineId = declaration.PipelineId, Kind = SubscriptionKind.Channel,
                Mode = DeliveryMode.Pull, Context = JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(declaration.Context, TestWireJson.Options))!,
                Schema = declaration.Schema, FilterKey = declaration.FilterKey, Backfill = declaration.Backfill, State = SubscriptionState.Active, CreatedAtUtc = now,
            };
            var entry = new Entry(subscription, declaration.ContainerId, pipeline.SourceId);
            subscriptions.Add(subscription.Id, entry);
            if (declaration.Backfill != BackfillMode.None)
                foreach (var post in posts.Snapshot(declaration.ContainerId).Where(post => post.State == PostState.Live && post.MergedInto is null
                             && post.Links.Any(link => link.SourceId == pipeline.SourceId)
                             && (declaration.BackfillSinceUtc is null || post.FirstSeenAtUtc >= declaration.BackfillSinceUtc)))
                    Append(entry, post, "post.new", null, null, "backfill");
            return Result<SubscriptionModel>.Ok(subscription);
        }
    }

    /// <inheritdoc />
    public Task<Result<SubscriptionModel>> GetSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (posts.Gate) return Task.FromResult(subscriptions.TryGetValue(subscriptionId, out var entry)
            ? Result<SubscriptionModel>.Ok(entry.Subscription) : Missing<SubscriptionModel>("Subscription not found."));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<SubscriptionModel>>> ListSubscriptionsAsync(Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (posts.Gate) return Task.FromResult(Result<IReadOnlyList<SubscriptionModel>>.Ok(subscriptions.Values.Select(entry => entry.Subscription)
            .Where(subscription => tenantId is null || subscription.TenantId == tenantId).OrderBy(subscription => subscription.CreatedAtUtc).ThenBy(subscription => subscription.Id).ToArray()));
    }

    /// <inheritdoc />
    public Task<Result<CursorPage<DeliveryEvent>>> PullAsync(Guid subscriptionId, string? cursor = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (limit is < 1 or > 500) return Task.FromResult(Invalid<CursorPage<DeliveryEvent>>("The limit must be between 1 and 500."));
        if (!TryCursor(subscriptionId, cursor, out var offset)) return Task.FromResult(Invalid<CursorPage<DeliveryEvent>>("Malformed cursor or wrong subscription."));
        lock (posts.Gate)
        {
            if (!subscriptions.TryGetValue(subscriptionId, out var entry)) return Task.FromResult(Missing<CursorPage<DeliveryEvent>>("Subscription not found."));
            var data = entry.Events.Skip(offset).Take(limit).Select(item => item with { Data = Render(item.Data, entry.Subscription.Schema) }).ToArray();
            return Task.FromResult(Result<CursorPage<DeliveryEvent>>.Ok(new CursorPage<DeliveryEvent>
            {
                Data = data, Next = data.Length == 0 && cursor is not null ? cursor : Encode(subscriptionId, offset + data.Length), HasMore = offset + data.Length < entry.Events.Count,
            }));
        }
    }

    private void Observe(PostModel post, string type, Guid? runId, Guid? linkId)
    {
        lock (posts.Gate)
            foreach (var entry in subscriptions.Values.Where(entry => entry.ContainerId == post.ContainerId
                         && (post.Links.Any(link => link.SourceId == entry.SourceId) || entry.Events.Any(item => item.PostId == post.Id))
                         && (runId is null || posts.PipelineFor(runId) == entry.Subscription.PipelineId || entry.Events.Any(item => item.PostId == post.Id))))
                Append(entry, post, type, runId, linkId, "ingest");
    }

    private void Append(Entry entry, PostModel post, string type, Guid? runId, Guid? linkId, string reason)
    {
        var previous = entry.Events.LastOrDefault(item => item.PostId == post.Id);
        var followsLifecycle = previous is not null && (type is DeliveryEventTypes.LinkState or DeliveryEventTypes.PostMerged
            or DeliveryEventTypes.PostUnmerged or DeliveryEventTypes.PostPurged or DeliveryEventTypes.PostRelisted
            || previous.Data?.GetProperty("state").GetString() != post.State.ToString().ToLowerInvariant());
        if (!followsLifecycle && !Matches(entry.Subscription.FilterKey, post.Base)) return;
        var itemId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{post.Id:N}:{post.Revision}:{type}"))).ToLowerInvariant();
        if (entry.Events.Any(item => item.ItemId == itemId && item.Type == type)) return;
        entry.Events.Add(new DeliveryEvent
        {
            EventId = Guid.CreateVersion7(time.GetUtcNow()), ItemId = itemId, SubscriptionId = entry.Subscription.Id, Type = type, OccurredAtUtc = time.GetUtcNow(),
            PostId = post.Id, Revision = post.Revision, LinkId = linkId, RunId = runId, Reason = reason, Context = entry.Subscription.Context,
            Data = JsonSerializer.SerializeToElement(post, TestWireJson.Options),
        });
    }

    private bool Matches(string? key, PostEnvelope envelope)
    {
        if (key is null) return true;
        if (!postFilters.TryGetValue(key, out var filter)) return false;
        try { return filter.Matches(envelope); }
        catch (Exception) { return false; }
    }

    private static JsonElement? Render(JsonElement? data, string? schema)
    {
        if (schema != "wow2.event.v1" || data is not { } payload || !payload.TryGetProperty("base", out var envelope)
            || !envelope.TryGetProperty("extras", out var extras) || !extras.TryGetProperty("event", out var mapped) || mapped.ValueKind != JsonValueKind.Object) return data;
        var values = mapped.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
        if (values.TryGetValue("links", out var contentLinks)) values["contentLinks"] = contentLinks;
        foreach (var (source, target) in new[] { ("id", "postId"), ("revision", "revision"), ("state", "state"), ("links", "links"), ("fieldSources", "fieldSources"), ("mergedInto", "mergedInto") })
            if (payload.TryGetProperty(source, out var value)) values[target] = value.Clone();
        return JsonSerializer.SerializeToElement(values);
    }

    private static string Encode(Guid subscriptionId, int offset) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{subscriptionId:N}:{offset}"));
    private static bool TryCursor(Guid subscriptionId, string? cursor, out int offset)
    {
        offset = 0;
        if (string.IsNullOrEmpty(cursor)) return true;
        if (cursor.Length > 256) return false;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(':');
            return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out var id) && id == subscriptionId && int.TryParse(parts[1], out offset) && offset >= 0;
        }
        catch (FormatException) { return false; }
    }

    private static Result<T> Invalid<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.Validation(message));
    private static Result<T> Missing<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.NotFound(message));
    private sealed record Entry(SubscriptionModel Subscription, Guid ContainerId, Guid SourceId)
    {
        internal List<DeliveryEvent> Events { get; } = [];
    }
}
