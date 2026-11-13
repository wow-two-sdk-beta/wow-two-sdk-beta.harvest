using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

/// <summary>Applies a marketplace profile: the consumer's tenant, its private container, the marketplace source, one
/// pipeline and one listing subscription per saved search, and the consumer's key. Existing rows and operator settings
/// are kept.</summary>
public sealed class MarketplaceProvisioner(IRegistrationBroker registry, IDeliveryBroker delivery)
{
    private const string SourceHandle = "www.olx.uz";
    private const string SourceKey = "olx-uz";
    private const string SourceName = "OLX Uzbekistan";
    private const string SourceGroup = "OLX";
    private const string CheckPolicy = "web-direct";

    private static readonly SemaphoreSlim SetupGate = new(1, 1);

    /// <summary>Creates what the profile names and is missing. Schedules start disabled unless requested; a request also
    /// switches on the profile's cadences of pipelines that already exist, keeping an operator-tuned interval.</summary>
    /// <remarks>Repeat calls reuse identities and issue no key unless <paramref name="keys"/> asks. Call setup through one
    /// operator host at a time; the registry rejects conflicting creates.</remarks>
    /// <param name="profile">The profile to apply.</param>
    /// <param name="enableSchedules">Whether the capture and check cadences run.</param>
    /// <param name="keys">Whether to issue or rotate the consumer's key.</param>
    /// <param name="cancellationToken">The token that cancels setup.</param>
    /// <returns>The IDs, and a key secret only when this call issued one.</returns>
    public async Task<Result<MarketplaceSetup>> EnsureAsync(MarketplaceProfile profile, bool enableSchedules = false,
        MarketplaceKeyAction keys = MarketplaceKeyAction.None, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Searches.Count == 0 || profile.Searches.Select(search => search.Key).Distinct(StringComparer.Ordinal).Count() != profile.Searches.Count)
            return Invalid("A marketplace profile needs one or more searches with distinct keys.");

        await SetupGate.WaitAsync(cancellationToken);
        try
        {
            var tenants = Value(await registry.ListTenantsAsync(cancellationToken));
            TenantModel tenant = tenants.SingleOrDefault(item => item.Slug == profile.TenantSlug) ?? Value(await registry.RegisterTenantAsync(
                new() { Slug = profile.TenantSlug, Name = profile.TenantName, PlatformCredentialsAllowed = false }, cancellationToken));
            ContainerModel container = Value(await registry.ListContainersAsync(tenant.Id, cancellationToken))
                .SingleOrDefault(item => item.ExternalRef == profile.ContainerRef)
                ?? Value(await registry.RegisterContainerAsync(new()
                {
                    Kind = ContainerKind.Private, TenantId = tenant.Id, ExternalRef = profile.ContainerRef, EmbeddingsEnabled = false,
                }, cancellationToken));
            if (container.Kind != ContainerKind.Private || container.TenantId != tenant.Id || container.EmbeddingsEnabled)
                return Conflict($"The existing {profile.ContainerRef} container is not private, the tenant's own and non-embedded.");

            SourceModel source = await EnsureSourceAsync(container.Id, cancellationToken);
            var pipelines = Value(await registry.ListPipelinesAsync(container.Id, cancellationToken)).ToList();
            var subscriptions = Value(await delivery.ListSubscriptionsAsync(tenant.Id, cancellationToken)).ToList();
            var rows = new List<MarketplaceSearchRegistration>();
            foreach (MarketplaceSearch search in profile.Searches)
            {
                var schedules = Cadence(profile, enableSchedules);
                PipelineModel? pipeline = pipelines.SingleOrDefault(item => item.SourceId == source.Id && item.Scope.Url == search.Url);
                if (pipeline is not null && enableSchedules && EnabledCadence(pipeline, schedules) is { } cadence)
                {
                    pipeline = Value(await registry.UpdatePipelineAsync(pipeline.Id, new()
                    {
                        Name = pipeline.Name, Scope = pipeline.Scope, Schedules = cadence,
                    }, cancellationToken));
                    pipelines[pipelines.FindIndex(item => item.Id == pipeline.Id)] = pipeline;
                }
                if (pipeline is null)
                {
                    pipeline = Value(await registry.DeclarePipelineAsync(new()
                    {
                        ContainerId = container.Id, SourceId = source.Id, Scope = new() { Url = search.Url }, Name = search.Name,
                        Ai = AiMode.Off, Pinned = false, Schedules = schedules, Limits = profile.Limits,
                    }, cancellationToken));
                    pipelines.Add(pipeline);
                }
                if (pipeline.Ai != AiMode.Off)
                    return Conflict($"The existing {search.Key} pipeline enables AI. Review it before applying this profile.");

                SubscriptionModel? subscription = subscriptions.FirstOrDefault(item =>
                    item.PipelineId == pipeline.Id && item.FilterKey == MarketplaceListingFilter.FilterKey
                    && item.Schema == PostEnvelopeConstants.Schema && item.State == SubscriptionState.Active
                    && item.Context.TryGetValue("catalog", out string? catalog) && catalog == profile.Catalog);
                if (subscription is null)
                {
                    subscription = Value(await delivery.SubscribeAsync(new()
                    {
                        TenantId = tenant.Id, ContainerId = container.Id, PipelineId = pipeline.Id,
                        FilterKey = MarketplaceListingFilter.FilterKey, Schema = PostEnvelopeConstants.Schema,
                        Context = new Dictionary<string, string> { ["catalog"] = profile.Catalog, ["search"] = search.Key },
                        Backfill = BackfillMode.Live,
                    }, cancellationToken));
                    subscriptions.Add(subscription);
                }
                rows.Add(new(search.Key, search.Url, pipeline.Id, subscription.Id));
            }

            MarketplaceConsumerKey? key = await EnsureKeyAsync(tenant.Id, profile.Consumer, keys, cancellationToken);
            return Result<MarketplaceSetup>.Ok(new(profile.Key, tenant.Id, container.Id, source.Id, rows, key));
        }
        catch (ProvisioningFailure failure) { return Result<MarketplaceSetup>.Fail(failure.Error); }
        finally { SetupGate.Release(); }
    }

    /// <summary>Reads what setup owns for a profile, writing nothing.</summary>
    /// <remarks>A tenant or container setup has not created answers <c>NotFound</c>; a search without a pipeline reads as a
    /// row with no pipeline.</remarks>
    /// <param name="profile">The profile to read.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The tenant, container and one row per search.</returns>
    public async Task<Result<MarketplaceStatus>> ReadAsync(MarketplaceProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        try
        {
            TenantModel? tenant = Value(await registry.ListTenantsAsync(cancellationToken)).SingleOrDefault(item => item.Slug == profile.TenantSlug);
            ContainerModel? container = tenant is null ? null
                : Value(await registry.ListContainersAsync(tenant.Id, cancellationToken)).SingleOrDefault(item => item.ExternalRef == profile.ContainerRef);
            if (tenant is null || container is null)
                return Result<MarketplaceStatus>.Fail(AppErrorFactory.NotFound(
                    $"Tenant {profile.TenantSlug} has no {profile.ContainerRef} container yet; run marketplace setup first."));
            var pipelines = Value(await registry.ListPipelinesAsync(container.Id, cancellationToken));
            var subscriptions = Value(await delivery.ListSubscriptionsAsync(tenant.Id, cancellationToken));
            var rows = profile.Searches.Select(search =>
            {
                PipelineModel? pipeline = pipelines.FirstOrDefault(item => item.Scope.Url == search.Url);
                Guid? subscription = pipeline is null ? null : subscriptions.FirstOrDefault(item => item.PipelineId == pipeline.Id
                    && item.FilterKey == MarketplaceListingFilter.FilterKey && item.State == SubscriptionState.Active)?.Id;
                return new MarketplaceSearchStatus(search.Key, pipeline, subscription);
            }).ToList();
            int active = Value(await registry.ListApiKeysAsync(tenant.Id, cancellationToken))
                .Count(item => item.Name == profile.Consumer && item.State == ApiKeyState.Active);
            return Result<MarketplaceStatus>.Ok(new(profile.Key, tenant.Id, container.Id, rows, active));
        }
        catch (ProvisioningFailure failure) { return Result<MarketplaceStatus>.Fail(failure.Error); }
    }

    /// <summary>Creates or reuses the container's OLX source, named and grouped for the console; an operator's name or
    /// group is kept.</summary>
    private async Task<SourceModel> EnsureSourceAsync(Guid containerId, CancellationToken cancellationToken)
    {
        var sources = Value(await registry.ListSourcesAsync(containerId, cancellationToken));
        SourceModel source = sources.SingleOrDefault(item => item.Family == SourceFamily.Site && item.Handle == SourceHandle)
            ?? Value(await registry.RegisterSourceAsync(new()
            {
                ContainerId = containerId, Family = SourceFamily.Site, Handle = SourceHandle, ExternalRef = SourceKey,
                Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window, Posture = SourcePosture.OwnFetch,
                AiAllowed = false, PoolShareable = false, RobotsPolicy = RobotsPolicy.Obey, CheckPolicy = CheckPolicy,
            }, cancellationToken));
        if (source.AiAllowed || source.PoolShareable || source.RobotsPolicy != RobotsPolicy.Obey)
            throw new ProvisioningFailure(AppErrorFactory.Conflict(
                "The existing OLX source has a different AI, sharing or robots posture. Review it before applying this profile."));
        if (source.GroupId is not null && source.Name is not null)
            return source;

        var groups = Value(await registry.ListSourceGroupsAsync(containerId, cancellationToken));
        SourceGroupModel group = groups.FirstOrDefault(item => string.Equals(item.Name, SourceGroup, StringComparison.OrdinalIgnoreCase))
            ?? Value(await registry.DeclareSourceGroupAsync(new() { ContainerId = containerId, Name = SourceGroup }, cancellationToken));
        return Value(await registry.UpdateSourceAsync(source.Id, new()
        {
            Name = source.Name is null ? SourceName : null,
            GroupId = source.GroupId is null ? group.Id : null,
        }, cancellationToken));
    }

    /// <summary>Issues or rotates the consumer's <c>events:read</c> key as asked, and reports the newest active one.</summary>
    private async Task<MarketplaceConsumerKey?> EnsureKeyAsync(Guid tenantId, string consumer, MarketplaceKeyAction action,
        CancellationToken cancellationToken)
    {
        var active = Value(await registry.ListApiKeysAsync(tenantId, cancellationToken))
            .Where(item => item.Name == consumer && item.State == ApiKeyState.Active)
            .OrderByDescending(item => item.CreatedAtUtc).ToList();
        if (action == MarketplaceKeyAction.Rotate)
        {
            foreach (ApiKeyModel key in active)
                Value(await registry.RevokeApiKeyAsync(tenantId, key.Id, cancellationToken));
            active.Clear();
        }
        if (action == MarketplaceKeyAction.None || active.Count > 0)
            return active.FirstOrDefault() is { } newest ? new(newest.Id, newest.Prefix, null) : null;

        IssuedApiKeyModel issued = Value(await registry.IssueApiKeyAsync(tenantId, new()
        {
            Name = consumer, Scopes = [HarvestScopeConstants.EventsRead],
        }, cancellationToken));
        return new(issued.Key.Id, issued.Key.Prefix, issued.Secret);
    }

    /// <summary>The profile's capture and check cadence, switched on only when asked.</summary>
    private static List<ScheduleDeclaration> Cadence(MarketplaceProfile profile, bool enabled) =>
    [
        new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = profile.CaptureEvery, TimeZone = profile.TimeZone, Enabled = enabled },
        new() { Kind = WorkKind.Check, Mode = ScheduleMode.Interval, Every = profile.CheckEvery, TimeZone = profile.TimeZone, Enabled = enabled },
    ];

    /// <summary>Returns an existing pipeline's full cadence with the profile's rows switched on, or null when every such row
    /// already runs. An operator-tuned row keeps its timing; a missing row is added; every other row is carried, since an
    /// update disables the kinds it omits.</summary>
    private static List<ScheduleDeclaration>? EnabledCadence(PipelineModel pipeline, IReadOnlyList<ScheduleDeclaration> wanted)
    {
        var missing = wanted.Where(row => row.Enabled).ToDictionary(row => row.Kind);
        bool changed = false;
        var cadence = new List<ScheduleDeclaration>();
        foreach (ScheduleModel row in pipeline.Schedules)
        {
            bool enable = missing.Remove(row.Kind) && !row.Enabled;
            changed |= enable;
            cadence.Add(new()
            {
                Kind = row.Kind, Mode = row.Mode, Every = row.Every, MaxEvery = row.MaxEvery, Cron = row.Cron,
                TimeZone = row.TimeZone, Window = row.Window, Enabled = row.Enabled || enable,
            });
        }
        cadence.AddRange(missing.Values);
        return changed || missing.Count > 0 ? cadence : null;
    }

    private static T Value<T>(Result<T> result) where T : notnull => result switch
    {
        Result<T>.Success success => success.Value,
        Result<T>.Failure failure => throw new ProvisioningFailure(failure.Error),
        _ => throw new InvalidOperationException("Unexpected registry result."),
    };

    private static Result<MarketplaceSetup> Invalid(string message) => Result<MarketplaceSetup>.Fail(AppErrorFactory.Validation(message));
    private static Result<MarketplaceSetup> Conflict(string message) => Result<MarketplaceSetup>.Fail(AppErrorFactory.Conflict(message));

    private sealed class ProvisioningFailure(AppError error) : Exception(error.Message)
    {
        internal AppError Error { get; } = error;
    }
}
