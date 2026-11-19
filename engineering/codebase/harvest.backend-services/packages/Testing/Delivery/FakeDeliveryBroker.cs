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
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;

/// <summary>Implements pull subscriptions over a <see cref="FakePostBroker"/> for consumer tests: channels over one
/// pipeline and selectors over Layer 1 tags and sources, by the module's rules.</summary>
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
        if (declaration.Selector is not null) return await SubscribeSelectorAsync(declaration, cancellationToken);
        if (declaration.PipelineId is not { } pipelineId) return Invalid<SubscriptionModel>("A channel subscription needs its pipeline.");
        if (declaration.Name is not null) return Invalid<SubscriptionModel>("Only a selector subscription has a name.");
        if (Malformed(declaration)) return Invalid<SubscriptionModel>("The subscription is malformed.");
        if (declaration.FilterKey is { } filterKey && (string.IsNullOrWhiteSpace(filterKey) || filterKey.Length > 200
            || !(postFilters.ContainsKey(filterKey) || DeliveryTagFilter.IsValid(filterKey))))
            return Invalid<SubscriptionModel>("The requested post filter is not registered or its key is invalid.");
        if (await registry.GetTenantAsync(declaration.TenantId, cancellationToken) is not Result<TenantModel>.Success { Value.State: TenantState.Active })
            return Missing<SubscriptionModel>("Active tenant not found.");
        if (await registry.GetContainerAsync(declaration.ContainerId, cancellationToken) is not Result<ContainerModel>.Success { Value: var container }
            || (container.Kind != ContainerKind.Pool && container.TenantId != declaration.TenantId)
            || await registry.GetPipelineAsync(pipelineId, cancellationToken) is not Result<PipelineModel>.Success { Value: var pipeline }
            || pipeline.ContainerId != declaration.ContainerId)
            return Missing<SubscriptionModel>("Pipeline not found in an accessible container.");
        lock (posts.Gate)
        {
            var now = time.GetUtcNow();
            var subscription = new SubscriptionModel
            {
                Id = Guid.CreateVersion7(now), TenantId = declaration.TenantId, PipelineId = pipelineId, Kind = SubscriptionKind.Channel,
                Mode = DeliveryMode.Pull, Context = CopyContext(declaration.Context),
                Schema = declaration.Schema, FilterKey = declaration.FilterKey, Backfill = declaration.Backfill, State = SubscriptionState.Active, CreatedAtUtc = now,
            };
            var entry = new Entry(subscription, declaration.ContainerId, pipeline.SourceId);
            subscriptions.Add(subscription.Id, entry);
            if (declaration.Backfill != BackfillMode.None)
                foreach (var post in posts.Snapshot(declaration.ContainerId).Where(post => post.State == PostState.Live && post.MergedInto is null
                             && post.Links.Any(link => link.SourceId == pipeline.SourceId && pipeline.Scope.IncludesUrl(link.Url))
                             && (declaration.BackfillSinceUtc is null || post.FirstSeenAtUtc >= declaration.BackfillSinceUtc)))
                    AppendChannel(entry, post, "post.new", null, null, "backfill");
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
            var data = entry.Events.Skip(offset).Take(limit).Select(item => Rendered(entry, item)).ToArray();
            return Task.FromResult(Result<CursorPage<DeliveryEvent>>.Ok(new CursorPage<DeliveryEvent>
            {
                Data = data, Next = data.Length == 0 && cursor is not null ? cursor : Encode(subscriptionId, offset + data.Length), HasMore = offset + data.Length < entry.Events.Count,
            }));
        }
    }

    /// <inheritdoc />
    public async Task<Result<SelectorChangeModel>> SetSelectorAsync(Guid subscriptionId, SelectorChange change, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (change?.Selector is null) return Invalid<SelectorChangeModel>("A selector change needs its selector.");
        if (change.ChangedBy is { Length: > 200 }) return Invalid<SelectorChangeModel>("The changer's name is too long.");
        Entry? found;
        lock (posts.Gate) subscriptions.TryGetValue(subscriptionId, out found);
        if (found is null) return Missing<SelectorChangeModel>("Subscription not found.");
        if (found.Subscription.Kind != SubscriptionKind.Selector) return Invalid<SelectorChangeModel>("Only a selector subscription has a selector.");
        if (found.Subscription.State == SubscriptionState.Archived) return Conflict<SelectorChangeModel>("An archived subscription cannot change its selector.");
        var selector = SelectorRules.Normalize(change.Selector);
        if (SelectorRules.Validate(selector) is { } shape) return Invalid<SelectorChangeModel>(shape);
        if (await RefsProblemAsync(registry, found.ContainerId, selector, cancellationToken) is { } problem) return Invalid<SelectorChangeModel>(problem);

        lock (posts.Gate)
        {
            var entry = found;
            if (entry.Subscription.State == SubscriptionState.Archived) return Conflict<SelectorChangeModel>("An archived subscription cannot change its selector.");
            var now = time.GetUtcNow();
            var window = change.WindowSinceUtc?.ToUniversalTime() ?? now.AddDays(-SelectorRules.DefaultWindowDays);
            if (window > now) return Invalid<SelectorChangeModel>("The window's lower bound cannot be in the future.");
            if (window < now.AddDays(-SelectorRules.MaxWindowDays))
                return Invalid<SelectorChangeModel>($"The window cannot reach back more than {SelectorRules.MaxWindowDays} days.");
            if (Same(selector, entry.Subscription.Selector!))
                return Result<SelectorChangeModel>.Ok(new SelectorChangeModel { Subscription = entry.Subscription, Changed = false });

            var candidates = posts.Snapshot(entry.ContainerId).Where(post => post.MergedInto is null
                    && (entry.Events.Any(item => item.PostId == post.Id) || (post.State == PostState.Live && post.LastChangedAtUtc >= window)))
                .OrderBy(post => post.FirstSeenAtUtc).ThenBy(post => post.Id).ToList();
            if (candidates.Count > SelectorRules.MaxWindowPosts)
                return Invalid<SelectorChangeModel>($"The window holds more than {SelectorRules.MaxWindowPosts} posts; narrow it.");
            int version = entry.Subscription.SelectorVersion!.Value + 1;
            entry.Subscription = entry.Subscription with { Selector = selector, SelectorVersion = version };
            var scope = ReadScope(registry, entry.ContainerId, [selector]);
            string suffix = $":s{version}";
            int considered = 0, newMatches = 0, unmatched = 0, rematched = 0, pending = 0;
            foreach (var post in candidates)
            {
                considered++;
                var evaluation = SubscriptionSelectorMatcher.Evaluate(selector, Facts(post, scope.Groups), scope.Sections, entry.Subscription.Schema, entry.Subscription.Context);
                if (evaluation.Verdict == SelectorVerdict.Pending) { pending++; continue; }
                var (seen, wasUnmatched, _) = History(entry, post);
                var (type, reason) = !seen ? (evaluation.Verdict == SelectorVerdict.Match ? DeliveryEventTypes.PostNew : null, "backfill")
                    : !wasUnmatched ? (evaluation.Verdict == SelectorVerdict.NoMatch ? DeliveryEventTypes.PostUnmatched : null, "resync")
                    : (evaluation.Verdict == SelectorVerdict.Match ? DeliveryEventTypes.PostChanged : null, "resync");
                if (type is null || !AppendSelector(entry, post, type, null, null, reason, version, evaluation, suffix)) continue;
                if (type == DeliveryEventTypes.PostNew) newMatches++;
                else if (type == DeliveryEventTypes.PostUnmatched) unmatched++;
                else rematched++;
            }
            return Result<SelectorChangeModel>.Ok(new SelectorChangeModel
            {
                Subscription = entry.Subscription, Changed = true, WindowSinceUtc = window, Considered = considered, NewMatches = newMatches,
                Unmatched = unmatched, Rematched = rematched, Pending = pending,
            });
        }
    }

    /// <inheritdoc />
    public Task<Result<SubscriptionModel>> SetStateAsync(Guid subscriptionId, SubscriptionState state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(state)) return Task.FromResult(Invalid<SubscriptionModel>("The subscription state is not one of active, suspended or archived."));
        lock (posts.Gate)
        {
            if (!subscriptions.TryGetValue(subscriptionId, out var entry)) return Task.FromResult(Missing<SubscriptionModel>("Subscription not found."));
            if (entry.Subscription.State != state)
            {
                if (entry.Subscription.State == SubscriptionState.Archived) return Task.FromResult(Conflict<SubscriptionModel>("An archived subscription stays archived."));
                entry.Subscription = entry.Subscription with { State = state };
            }
            return Task.FromResult(Result<SubscriptionModel>.Ok(entry.Subscription));
        }
    }

    /// <summary>Subscribes a tenant to a selector over one container and backfills the live posts it matches.</summary>
    private async Task<Result<SubscriptionModel>> SubscribeSelectorAsync(SubscriptionDeclaration declaration, CancellationToken cancellationToken)
    {
        if (declaration.PipelineId is not null) return Invalid<SubscriptionModel>("A selector subscription follows no pipeline; its sources are in the selector.");
        if (declaration.FilterKey is not null) return Invalid<SubscriptionModel>("A selector subscription takes no filter key; its selector is the filter.");
        if (!SelectorRules.IsName(declaration.Name))
            return Invalid<SubscriptionModel>("A selector subscription needs a name: a lowercase letter or digit, then up to 63 letters, digits or hyphens.");
        if (Malformed(declaration)) return Invalid<SubscriptionModel>("The subscription is malformed.");
        var selector = SelectorRules.Normalize(declaration.Selector!);
        if (SelectorRules.Validate(selector) is { } shape) return Invalid<SubscriptionModel>(shape);
        if (await registry.GetTenantAsync(declaration.TenantId, cancellationToken) is not Result<TenantModel>.Success { Value.State: TenantState.Active })
            return Missing<SubscriptionModel>("Active tenant not found.");
        if (await registry.GetContainerAsync(declaration.ContainerId, cancellationToken) is not Result<ContainerModel>.Success { Value: var container }
            || (container.Kind != ContainerKind.Pool && container.TenantId != declaration.TenantId))
            return Missing<SubscriptionModel>("Container not found or not accessible to the tenant.");
        lock (posts.Gate)
            if (NameTaken(declaration.TenantId, declaration.Name!))
                return Conflict<SubscriptionModel>($"The tenant already has a subscription named '{declaration.Name}'.");
        if (await RefsProblemAsync(registry, declaration.ContainerId, selector, cancellationToken) is { } problem) return Invalid<SubscriptionModel>(problem);

        lock (posts.Gate)
        {
            if (NameTaken(declaration.TenantId, declaration.Name!))
                return Conflict<SubscriptionModel>($"The tenant already has a subscription named '{declaration.Name}'.");
            var now = time.GetUtcNow();
            var subscription = new SubscriptionModel
            {
                Id = Guid.CreateVersion7(now), TenantId = declaration.TenantId, Kind = SubscriptionKind.Selector, PipelineId = null, Name = declaration.Name,
                Selector = selector, SelectorVersion = 1, Mode = DeliveryMode.Pull, Context = CopyContext(declaration.Context),
                Schema = declaration.Schema, Backfill = declaration.Backfill, State = SubscriptionState.Active, CreatedAtUtc = now,
            };
            var entry = new Entry(subscription, declaration.ContainerId, null);
            subscriptions.Add(subscription.Id, entry);
            if (declaration.Backfill != BackfillMode.None)
            {
                var scope = ReadScope(registry, declaration.ContainerId, [selector]);
                var since = declaration.Backfill == BackfillMode.Since ? declaration.BackfillSinceUtc : null;
                foreach (var post in posts.Snapshot(declaration.ContainerId).Where(post => post.State == PostState.Live && post.MergedInto is null
                                 && (since is null || post.FirstSeenAtUtc >= since)).OrderBy(post => post.FirstSeenAtUtc).ThenBy(post => post.Id))
                {
                    var evaluation = SubscriptionSelectorMatcher.Evaluate(selector, Facts(post, scope.Groups), scope.Sections, subscription.Schema, subscription.Context);
                    if (evaluation.Verdict == SelectorVerdict.Match)
                        AppendSelector(entry, post, DeliveryEventTypes.PostNew, null, null, "backfill", 1, evaluation, string.Empty);
                }
            }
            return Result<SubscriptionModel>.Ok(subscription);
        }
    }

    private bool NameTaken(Guid tenantId, string name) => subscriptions.Values.Any(entry => entry.Subscription.TenantId == tenantId
        && entry.Subscription.Name == name && entry.Subscription.State != SubscriptionState.Archived);

    private void Observe(PostModel post, string type, Guid? runId, Guid? linkId)
    {
        lock (posts.Gate)
        {
            foreach (var entry in subscriptions.Values.Where(entry => entry.Subscription.Kind == SubscriptionKind.Channel && entry.Subscription.State == SubscriptionState.Active
                         && entry.ContainerId == post.ContainerId
                         && (post.Links.Any(link => link.SourceId == entry.SourceId) || entry.Events.Any(item => item.PostId == post.Id))
                         && (runId is null || posts.PipelineFor(runId) == entry.Subscription.PipelineId || entry.Events.Any(item => item.PostId == post.Id))))
                AppendChannel(entry, post, type, runId, linkId, "ingest");

            // A selector follows tags and sources, not one pipeline, so every write of its container reaches it.
            var selectors = subscriptions.Values.Where(entry => entry.Subscription.Kind == SubscriptionKind.Selector && entry.Subscription.State == SubscriptionState.Active
                && entry.ContainerId == post.ContainerId).OrderBy(entry => entry.Subscription.CreatedAtUtc).ThenBy(entry => entry.Subscription.Id).ToList();
            if (selectors.Count == 0) return;
            var scope = ReadScope(registry, post.ContainerId, selectors.Select(entry => entry.Subscription.Selector!));
            var facts = Facts(post, scope.Groups);
            foreach (var entry in selectors)
            {
                var evaluation = SubscriptionSelectorMatcher.Evaluate(entry.Subscription.Selector!, facts, scope.Sections, entry.Subscription.Schema, entry.Subscription.Context);
                var (seen, unmatched, stateChanged) = History(entry, post);
                if (SubscriptionSelectorMatcher.Choose(type, evaluation.Verdict, seen, unmatched, stateChanged) is { } delivered)
                    AppendSelector(entry, post, delivered, runId, linkId, "ingest", entry.Subscription.SelectorVersion!.Value, evaluation, string.Empty);
            }
        }
    }

    private void AppendChannel(Entry entry, PostModel post, string type, Guid? runId, Guid? linkId, string reason)
    {
        // The module's rule: a passing revision goes out; a held post keeps its lifecycle events; its first failing
        // revision goes out once as post.unmatched and later failing ones wait for a pass; an undecided filter keeps
        // state changes only.
        var previous = entry.Events.LastOrDefault(item => item.PostId == post.Id);
        var revision = entry.Events.LastOrDefault(item => item.PostId == post.Id && item.Type is DeliveryEventTypes.PostNew
            or DeliveryEventTypes.PostChanged or DeliveryEventTypes.PostExtracted or DeliveryEventTypes.PostUnmatched);
        var lifecycle = type is DeliveryEventTypes.LinkState or DeliveryEventTypes.PostMerged or DeliveryEventTypes.PostUnmerged
            or DeliveryEventTypes.PostPurged or DeliveryEventTypes.PostRelisted or DeliveryEventTypes.PostUnmatched;
        var verdict = Evaluate(entry.Subscription.FilterKey, post.Base);
        string? delivered = verdict == true ? type
            : previous is null ? null
            : lifecycle ? type
            : revision?.Type == DeliveryEventTypes.PostUnmatched ? null
            : verdict == false ? DeliveryEventTypes.PostUnmatched
            : previous.Data?.GetProperty("state").GetString() != post.State.ToString().ToLowerInvariant() ? type
            : null;
        if (delivered is null) return;
        type = delivered;
        var itemId = ItemId(post, type, string.Empty);
        if (entry.Events.Any(item => item.ItemId == itemId && item.Type == type)) return;
        entry.Events.Add(new DeliveryEvent
        {
            EventId = Guid.CreateVersion7(time.GetUtcNow()), ItemId = itemId, SubscriptionId = entry.Subscription.Id, PipelineId = entry.Subscription.PipelineId, Type = type, OccurredAtUtc = time.GetUtcNow(),
            PostId = post.Id, Revision = post.Revision, LinkId = linkId, RunId = runId, Reason = reason, Context = entry.Subscription.Context,
            Data = JsonSerializer.SerializeToElement(post, TestWireJson.Options),
        });
    }

    /// <summary>Appends one event of a selector subscription with its verdict; a repeat of the same item and type writes
    /// nothing.</summary>
    private bool AppendSelector(Entry entry, PostModel post, string type, Guid? runId, Guid? linkId, string reason, int version, SelectorEvaluation evaluation, string suffix)
    {
        var itemId = ItemId(post, type, suffix);
        if (entry.Events.Any(item => item.ItemId == itemId && item.Type == type)) return false;
        var eventId = Guid.CreateVersion7(time.GetUtcNow());
        entry.Events.Add(new DeliveryEvent
        {
            EventId = eventId, ItemId = itemId, SubscriptionId = entry.Subscription.Id, Type = type, OccurredAtUtc = time.GetUtcNow(),
            PostId = post.Id, Revision = post.Revision, LinkId = linkId, RunId = runId, Reason = reason, Context = entry.Subscription.Context,
            Match = new SubscriptionMatch { SelectorVersion = version, Verdict = evaluation.Verdict, Clauses = evaluation.Clauses },
            Data = JsonSerializer.SerializeToElement(post, TestWireJson.Options),
        });
        entry.MatchContexts[eventId] = evaluation.Context;
        return true;
    }

    /// <summary>Reads what the log holds about a post, as the module's <c>previous</c> and <c>revision</c> laterals do: whether
    /// it holds any event of it, whether its newest content event is <c>post.unmatched</c>, and whether the post's state
    /// differs from the one its newest event carried.</summary>
    private static (bool Seen, bool Unmatched, bool StateChanged) History(Entry entry, PostModel post)
    {
        var previous = entry.Events.LastOrDefault(item => item.PostId == post.Id);
        var revision = entry.Events.LastOrDefault(item => item.PostId == post.Id && item.Type is DeliveryEventTypes.PostNew
            or DeliveryEventTypes.PostChanged or DeliveryEventTypes.PostExtracted or DeliveryEventTypes.PostUnmatched);
        return (previous is not null, revision?.Type == DeliveryEventTypes.PostUnmatched,
            previous?.Data?.GetProperty("state").GetString() != post.State.ToString().ToLowerInvariant());
    }

    private bool? Evaluate(string? key, PostEnvelope envelope)
    {
        if (key is null) return true;
        if (!postFilters.TryGetValue(key, out var filter)) return DeliveryTagFilter.IsValid(key) ? DeliveryTagFilter.Matches(key, envelope) : null;
        try { return filter.Matches(envelope); }
        catch (Exception) { return null; }
    }

    private bool Malformed(SubscriptionDeclaration declaration) =>
        !Enum.IsDefined(declaration.Backfill) || (declaration.Backfill == BackfillMode.Since) != declaration.BackfillSinceUtc.HasValue
        || declaration.BackfillSinceUtc > time.GetUtcNow() || declaration.Schema is { Length: > 200 }
        || declaration.Context.Count > 64 || declaration.Context.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 256 || pair.Value is null || pair.Value.Length > 4096);

    private static Dictionary<string, string> CopyContext(IReadOnlyDictionary<string, string> context) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(context, TestWireJson.Options))!;

    private static string ItemId(PostModel post, string type, string suffix) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{post.Id:N}:{post.Revision}:{type}{suffix}"))).ToLowerInvariant();

    private static bool Same(SubscriptionSelector left, SubscriptionSelector right) =>
        JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left, TestWireJson.Options), JsonSerializer.SerializeToElement(right, TestWireJson.Options));

    /// <summary>Returns the event as a pull reads it: the payload rendered for the subscription's schema and, for a selector
    /// event, its top-level <c>context</c> set to the merged context of the refs that matched.</summary>
    private static DeliveryEvent Rendered(Entry entry, DeliveryEvent item)
    {
        var data = Render(item.Data, entry.Subscription.Schema);
        if (data is { } payload && entry.MatchContexts.TryGetValue(item.EventId, out var context))
            data = WithContext(payload, context);
        return item with { Data = data };
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

    private static JsonElement WithContext(JsonElement payload, IReadOnlyDictionary<string, string> context)
    {
        if (payload.ValueKind != JsonValueKind.Object) return payload;
        var values = payload.EnumerateObject().Where(property => property.Name != "context").ToDictionary(property => property.Name, property => property.Value.Clone());
        values["context"] = JsonSerializer.SerializeToElement(context);
        return JsonSerializer.SerializeToElement(values);
    }

    /// <summary>Returns what a selector judges of a post revision: its envelope and links in the post's order, each link with
    /// the group of its source; the context link a selector prefers is the one that supplied the post's title.</summary>
    internal static SelectorPostFacts Facts(PostModel post, IReadOnlyDictionary<Guid, Guid?> groups) => new()
    {
        Envelope = post.Base,
        Links = [.. post.Links.Select(link => new SelectorLink(link.Id, link.SourceId, groups.GetValueOrDefault(link.SourceId), link.Url))],
        PrimaryLinkId = post.FieldSources.TryGetValue("title", out var titleLink) ? titleLink : null,
    };

    /// <summary>Reads the group of each source of the container and the section each referenced pipeline names.</summary>
    /// <remarks>The caller holds the post store's lock inside a post write, where nothing can await; the fake registry
    /// answers from memory, so these reads complete at once.</remarks>
    internal static (IReadOnlyDictionary<Guid, Guid?> Groups, IReadOnlyDictionary<Guid, SelectorSection> Sections) ReadScope(
        IRegistrationBroker registry, Guid containerId, IEnumerable<SubscriptionSelector> selectors)
    {
        var groups = registry.ListSourcesAsync(containerId).GetAwaiter().GetResult() is Result<IReadOnlyList<SourceModel>>.Success { Value: var sources }
            ? sources.ToDictionary(source => source.Id, source => source.GroupId) : [];
        var sections = new Dictionary<Guid, SelectorSection>();
        foreach (var id in selectors.SelectMany(Refs).Where(reference => reference.PipelineId.HasValue).Select(reference => reference.PipelineId!.Value).Distinct())
            if (registry.GetPipelineAsync(id).GetAwaiter().GetResult() is Result<PipelineModel>.Success { Value: var pipeline } && pipeline.ContainerId == containerId)
                sections[id] = new SelectorSection(pipeline.Id, pipeline.SourceId, pipeline.Scope);
        return (groups, sections);
    }

    /// <summary>Returns the problem in a normalized selector that needs the registry, or null: every source ref must name a
    /// group, source or pipeline of the container. Taxonomy keys are the module's alone to check.</summary>
    internal static async Task<string?> RefsProblemAsync(IRegistrationBroker registry, Guid containerId, SubscriptionSelector selector, CancellationToken ct)
    {
        var refs = Refs(selector).ToList();
        if (refs.Count == 0) return null;
        var groups = await registry.ListSourceGroupsAsync(containerId, ct) is Result<IReadOnlyList<SourceGroupModel>>.Success { Value: var listed }
            ? listed.Select(group => group.Id).ToHashSet() : [];
        foreach (var id in refs.Where(reference => reference.GroupId.HasValue).Select(reference => reference.GroupId!.Value).Distinct())
            if (!groups.Contains(id)) return $"Source group {id} is not in the subscription's container.";
        foreach (var id in refs.Where(reference => reference.SourceId.HasValue).Select(reference => reference.SourceId!.Value).Distinct())
            if (await registry.GetSourceAsync(id, ct) is not Result<SourceModel>.Success { Value: var source } || source.ContainerId != containerId)
                return $"Source {id} is not in the subscription's container.";
        foreach (var id in refs.Where(reference => reference.PipelineId.HasValue).Select(reference => reference.PipelineId!.Value).Distinct())
            if (await registry.GetPipelineAsync(id, ct) is not Result<PipelineModel>.Success { Value: var pipeline } || pipeline.ContainerId != containerId)
                return $"Pipeline {id} is not in the subscription's container.";
        return null;
    }

    private static IEnumerable<SelectorSourceRef> Refs(SubscriptionSelector selector) =>
        (selector.Sources?.Include ?? []).Concat(selector.Sources?.Exclude ?? []);

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
    private static Result<T> Conflict<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.Conflict(message));
    private sealed class Entry(SubscriptionModel subscription, Guid containerId, Guid? sourceId)
    {
        internal SubscriptionModel Subscription { get; set; } = subscription;
        internal Guid ContainerId { get; } = containerId;
        internal Guid? SourceId { get; } = sourceId;
        internal List<DeliveryEvent> Events { get; } = [];
        internal Dictionary<Guid, IReadOnlyDictionary<string, string>> MatchContexts { get; } = [];
    }
}
