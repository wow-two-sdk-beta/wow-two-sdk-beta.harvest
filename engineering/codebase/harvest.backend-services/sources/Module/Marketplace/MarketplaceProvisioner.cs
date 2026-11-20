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

/// <summary>Applies a marketplace profile: the consumer's tenant, its private container, sources by marketplace and intake mode,
/// one pipeline and one listing subscription per saved search, and the consumer's key. Existing rows and operator settings
/// are kept.</summary>
public sealed class MarketplaceProvisioner(IRegistrationBroker registry, IDeliveryBroker delivery)
{
    private static readonly SemaphoreSlim SetupGate = new(1, 1);

    /// <summary>Creates what the profile names and is missing. Schedules start disabled unless requested; a request also
    /// switches on the profile's cadences of pipelines that already exist, keeping an operator-tuned interval.</summary>
    /// <remarks>Repeat calls reuse identities and issue no key unless <paramref name="keys"/> asks. Call setup through one
    /// operator host at a time; the registry rejects conflicting creates. Collected sources start closed to models
    /// (<see cref="ModelProcessingMode.Off"/>); <paramref name="modelProcessing"/> sets the mode of every collected source, new or
    /// existing, as <c>ocharo setup --model-processing</c> does, and without it an existing source keeps the mode it has.
    /// <paramref name="allowAi"/> alone is the older switch: it opens a closed collected source to
    /// <see cref="ModelProcessingMode.Auto"/> and never changes a <c>manual</c> or <c>auto</c> one. Import-only sources keep their
    /// submitter-consent posture and their mode.</remarks>
    /// <param name="profile">The profile to apply.</param>
    /// <param name="enableSchedules">Whether the capture and check cadences run.</param>
    /// <param name="keys">Whether to issue or rotate the consumer's key.</param>
    /// <param name="allowAi">The older switch that opens closed collected sources to <see cref="ModelProcessingMode.Auto"/>; with
    /// <paramref name="modelProcessing"/> it is refused unless that is <see cref="ModelProcessingMode.Auto"/>.</param>
    /// <param name="modelProcessing">The model-processing mode to set on the profile's collected sources, or none to keep each as it is.</param>
    /// <param name="cancellationToken">The token that cancels setup.</param>
    /// <returns>The IDs, and a key secret only when this call issued one.</returns>
    public async Task<Result<MarketplaceSetup>> EnsureAsync(MarketplaceProfile profile, bool enableSchedules = false,
        MarketplaceKeyAction keys = MarketplaceKeyAction.None, bool allowAi = false, ModelProcessingMode? modelProcessing = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (allowAi && modelProcessing is { } other && other != ModelProcessingMode.Auto)
            return Invalid("allowAi means model processing auto; ask for one or the other.");
        bool opensOnly = allowAi && modelProcessing is null;
        modelProcessing ??= allowAi ? ModelProcessingMode.Auto : null;
        if (profile.Searches.Count == 0 || profile.Searches.Select(search => search.Key).Distinct(StringComparer.Ordinal).Count() != profile.Searches.Count)
            return Invalid("A marketplace profile needs one or more searches with distinct keys.");
        if (profile.Searches.FirstOrDefault(search => !Uri.TryCreate(search.Url, UriKind.Absolute, out Uri? url)
                || url.Scheme != Uri.UriSchemeHttps || url.IdnHost != search.Site.Handle) is { } stray)
            return Invalid($"Search {stray.Key} must be an https URL on {stray.Site.Handle}.");

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

            var sources = new Dictionary<MarketplaceSite, SourceModel>();
            var pipelines = Value(await registry.ListPipelinesAsync(container.Id, cancellationToken)).ToList();
            var subscriptions = Value(await delivery.ListSubscriptionsAsync(tenant.Id, cancellationToken)).ToList();
            var rows = new List<MarketplaceSearchRegistration>();
            foreach (MarketplaceSearch search in profile.Searches)
            {
                if (!sources.TryGetValue(search.Site, out var source))
                    sources[search.Site] = source = await EnsureSourceAsync(container.Id, search.Site, modelProcessing, opensOnly, cancellationToken);
                bool canCapture = !search.Site.ImportOnly && source.State == SourceState.Active
                    && source.Permission is SourcePermission.Open or SourcePermission.Granted;
                var schedules = Cadence(profile, search.Site, enableSchedules && canCapture);
                PipelineModel? pipeline = pipelines.SingleOrDefault(item => item.SourceId == source.Id && item.Scope.Url == search.Url);
                if (pipeline is not null && enableSchedules && canCapture
                    && ScheduleProvisioning.EnabledCadence(pipeline, schedules) is { } cadence)
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
                rows.Add(new(search.Key, search.Url, source.Id, pipeline.Id, subscription.Id, search.Site.ImportOnly,
                    canCapture && pipeline.State == PipelineState.Active));
            }

            MarketplaceConsumerKey? key = await EnsureKeyAsync(tenant.Id, profile.Consumer, keys, cancellationToken);
            return Result<MarketplaceSetup>.Ok(new(profile.Key, tenant.Id, container.Id, rows, key));
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
            var sources = Value(await registry.ListSourcesAsync(container.Id, cancellationToken));
            var subscriptions = Value(await delivery.ListSubscriptionsAsync(tenant.Id, cancellationToken));
            var rows = profile.Searches.Select(search =>
            {
                var source = sources.SingleOrDefault(item => item.Handle == search.Site.Handle && item.Family == Family(search.Site));
                PipelineModel? pipeline = pipelines.FirstOrDefault(item => item.SourceId == source?.Id && item.Scope.Url == search.Url);
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

    /// <summary>Creates or reuses the container's source for a marketplace site, named and grouped for the console; an
    /// operator's name or group is kept, and a collected source takes the requested model-processing mode.</summary>
    private async Task<SourceModel> EnsureSourceAsync(Guid containerId, MarketplaceSite site, ModelProcessingMode? modelProcessing,
        bool opensOnly, CancellationToken cancellationToken)
    {
        var sources = Value(await registry.ListSourcesAsync(containerId, cancellationToken));
        var family = Family(site);
        var source = sources.SingleOrDefault(item => item.Family == family && item.Handle == site.Handle);

        ModelProcessingMode? requested = site.ImportOnly ? null : modelProcessing;

        source ??= Value(await registry.RegisterSourceAsync(new()
            {
                ContainerId = containerId, Family = family, Handle = site.Handle, ExternalRef = site.Key,
                Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window,
                Posture = site.ImportOnly ? SourcePosture.ConsentedIntake : SourcePosture.OwnFetch,
                Permission = site.ImportOnly ? SourcePermission.Restricted : SourcePermission.Open,
                ConsentBasis = site.ImportOnly ? ConsentBasis.Submitter : ConsentBasis.None,
                ModelProcessing = requested ?? ModelProcessingMode.Off, PoolShareable = false, RobotsPolicy = RobotsPolicy.Obey,
                CheckPolicy = site.CheckPolicy,
            }, cancellationToken));
        if (source.PoolShareable || source.RobotsPolicy != RobotsPolicy.Obey || (site.ImportOnly && source.AiAllowed))
            throw new ProvisioningFailure(AppErrorFactory.Conflict(
                $"The existing {site.Name} source has a different AI, sharing or robots posture. Review it before applying this profile."));
        if (site.ImportOnly && (source.Posture != SourcePosture.ConsentedIntake || source.Permission != SourcePermission.Restricted))
            throw new ProvisioningFailure(AppErrorFactory.Conflict($"The existing {site.Name} source is not restricted to imports."));
        bool setsMode = requested is { } mode && source.ModelProcessing != mode
            && (!opensOnly || source.ModelProcessing == ModelProcessingMode.Off);
        if (source.GroupId is not null && source.Name is not null && !setsMode)
            return source;

        Guid? groupId = null;
        if (source.GroupId is null)
        {
            var groups = Value(await registry.ListSourceGroupsAsync(containerId, cancellationToken));
            groupId = (groups.FirstOrDefault(item => string.Equals(item.Name, site.Group, StringComparison.OrdinalIgnoreCase))
                ?? Value(await registry.DeclareSourceGroupAsync(new() { ContainerId = containerId, Name = site.Group }, cancellationToken))).Id;
        }
        return Value(await registry.UpdateSourceAsync(source.Id, new()
        {
            Name = source.Name is null ? site.Name : null,
            GroupId = groupId,
            ModelProcessing = setsMode ? requested : null,
        }, cancellationToken));
    }

    private static SourceFamily Family(MarketplaceSite site) => site.ImportOnly ? SourceFamily.Manual : SourceFamily.Site;

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
    private static List<ScheduleDeclaration> Cadence(MarketplaceProfile profile, MarketplaceSite site, bool enabled)
    {
        if (site.ImportOnly)
            return [];
        var schedules = new List<ScheduleDeclaration>
        {
            new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = profile.CaptureEvery, TimeZone = profile.TimeZone, Enabled = enabled },
        };
        if (site.CheckPolicy != "none")
            schedules.Add(new() { Kind = WorkKind.Check, Mode = ScheduleMode.Interval, Every = profile.CheckEvery, TimeZone = profile.TimeZone, Enabled = enabled });
        return schedules;
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
