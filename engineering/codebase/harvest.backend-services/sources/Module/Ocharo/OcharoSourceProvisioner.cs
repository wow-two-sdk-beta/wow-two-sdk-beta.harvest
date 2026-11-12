using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

/// <summary>Explicitly registers reviewed channels; existing operator settings are preserved.</summary>
public sealed class OcharoSourceProvisioner(IRegistrationBroker registry, IDeliveryBroker delivery)
{
    private static readonly SemaphoreSlim SetupGate = new(1, 1);

    /// <summary>Holds the slug of the tenant that owns Ocharo's sources by default.</summary>
    public const string DefaultTenantSlug = "ocharo";

    /// <summary>Creates missing registry rows and filtered subscriptions. Cadences start disabled unless requested; a
    /// request also switches on the registry cadences of pipelines that already exist. A source with no group joins
    /// its publisher's group, named as the registry names the publisher, and a source with no display name gets one;
    /// a group or name an operator set is kept.</summary>
    /// <remarks>Repeat calls reuse identities. Call setup through one operator host at a time; the registry rejects conflicting creates.
    /// <paramref name="tenantSlug"/> names the owning tenant, <c>ocharo</c> by default; another slug gives that tenant its own
    /// private container and subscriptions, such as an isolation probe.</remarks>
    public async Task<Result<OcharoSetup>> EnsureAsync(bool enableSchedules = false, CancellationToken cancellationToken = default,
        bool includeInstagram = false, IReadOnlyList<string>? groupIds = null, string tenantSlug = DefaultTenantSlug)
    {
        OcharoSourceRegistry catalog = OcharoSourceRegistry.Current;
        var selection = groupIds?.ToHashSet(StringComparer.Ordinal);
        if (selection is not null && (selection.Count == 0 || selection.Count != groupIds!.Count
            || selection.Any(id => !catalog.Groups.Any(group => group.Id == id))))
            return Invalid("Select one or more distinct publisher IDs from the Ocharo source registry.");

        var routes = catalog.Routes(includeInstagram, selection).ToArray();
        if (selection is not null)
        {
            var unavailable = selection.Except(routes.Select(route => route.Group.Id)).Order(StringComparer.Ordinal).ToArray();
            if (unavailable.Length > 0)
                return Invalid($"No selected collector for {string.Join(", ", unavailable)}. Check the channel mode and provider options in the registry.");
        }

        await SetupGate.WaitAsync(cancellationToken);
        try
        {
            var tenants = Value(await registry.ListTenantsAsync(cancellationToken));
            TenantModel tenant = tenants.SingleOrDefault(item => item.Slug == tenantSlug) ?? Value(await registry.RegisterTenantAsync(
                new()
                {
                    Slug = tenantSlug, Name = tenantSlug == DefaultTenantSlug ? "Ocharo events" : "Ocharo events · " + tenantSlug,
                    PlatformCredentialsAllowed = false,
                }, cancellationToken));
            var containers = Value(await registry.ListContainersAsync(tenant.Id, cancellationToken));
            ContainerModel container = containers.SingleOrDefault(item => item.ExternalRef == "ocharo-events")
                ?? Value(await registry.RegisterContainerAsync(new()
                {
                    Kind = ContainerKind.Private, TenantId = tenant.Id, ExternalRef = "ocharo-events", EmbeddingsEnabled = false,
                }, cancellationToken));
            if (container.Kind != ContainerKind.Private || container.TenantId != tenant.Id || container.EmbeddingsEnabled)
                return Conflict("The existing Ocharo container does not have the registry's private, non-embedded posture.");

            var sources = Value(await registry.ListSourcesAsync(container.Id, cancellationToken)).ToList();
            var pipelines = Value(await registry.ListPipelinesAsync(container.Id, cancellationToken)).ToList();
            var subscriptions = Value(await delivery.ListSubscriptionsAsync(tenant.Id, cancellationToken)).ToList();
            List<SourceGroupModel>? groups = null;
            var rows = new List<OcharoSourceRegistration>();
            foreach (var (group, channel, profile) in routes)
            {
                string seed = channel.PipelineUrl;
                var uri = new Uri(seed);
                bool instagram = profile.Family == SourceFamily.Instagram;
                string host = uri.IdnHost;
                SourceModel? source = sources.SingleOrDefault(item => item.Family == profile.Family && item.Handle == host);
                if (source is null)
                {
                    source = Value(await registry.RegisterSourceAsync(new()
                    {
                        ContainerId = container.Id, Family = profile.Family, Handle = host, ExternalRef = profile.Key,
                        Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window,
                        Posture = instagram ? SourcePosture.Official : SourcePosture.OwnFetch,
                        AiAllowed = false, PoolShareable = false, RobotsPolicy = RobotsPolicy.Obey, Authority = profile.Authority,
                        CheckPolicy = profile.CheckPolicy,
                    }, cancellationToken));
                    sources.Add(source);
                }
                if (source.AiAllowed || source.PoolShareable || source.RobotsPolicy != RobotsPolicy.Obey)
                    return Conflict("An existing source has a different AI, sharing or robots posture. Review it before applying this registry.");
                if (source.GroupId is null || source.Name is null)
                {
                    groups ??= Value(await registry.ListSourceGroupsAsync(container.Id, cancellationToken)).ToList();
                    SourceGroupModel publisher = groups.FirstOrDefault(item => string.Equals(item.Name, group.Name, StringComparison.OrdinalIgnoreCase))
                        ?? Value(await registry.DeclareSourceGroupAsync(new() { ContainerId = container.Id, Name = group.Name }, cancellationToken));
                    if (!groups.Contains(publisher))
                        groups.Add(publisher);
                    int index = sources.IndexOf(source);
                    source = Value(await registry.UpdateSourceAsync(source.Id, new()
                    {
                        Name = source.Name is null ? DisplayName(source) : null,
                        GroupId = source.GroupId is null ? publisher.Id : null,
                    }, cancellationToken));
                    sources[index] = source;
                }

                PipelineModel? pipeline = pipelines.SingleOrDefault(item => item.SourceId == source.Id && item.Scope.Url == seed);
                var schedules = new List<ScheduleDeclaration>
                {
                    new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(6), TimeZone = "Asia/Tashkent", Enabled = enableSchedules && !instagram },
                };
                if (profile.CheckPolicy == "web-direct")
                    schedules.Add(new() { Kind = WorkKind.Check, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(24), TimeZone = "Asia/Tashkent", Enabled = enableSchedules });
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
                        ContainerId = container.Id, SourceId = source.Id, Scope = new() { Url = seed },
                        Name = profile.Name + (instagram ? " · @" + uri.AbsolutePath.Trim('/') : "") + " · Ocharo",
                        Ai = AiMode.Off, Pinned = false, Schedules = schedules,
                        Limits = new() { MaxPages = 5, MaxItems = 150, MaxRequests = 180, MaxBytes = 30_000_000, MaxDuration = TimeSpan.FromMinutes(10), MaxProbes = 100 },
                    }, cancellationToken));
                    pipelines.Add(pipeline);
                }
                if (pipeline.Ai != AiMode.Off)
                    return Conflict("An existing Ocharo pipeline enables AI. Review it before using this registry.");

                SubscriptionModel? subscription = subscriptions.FirstOrDefault(item =>
                    item.PipelineId == pipeline.Id && item.FilterKey == OcharoFashionFilter.FilterKey
                    && item.Schema == PostEnvelopeConstants.EventSchema && item.State == SubscriptionState.Active
                    && item.Context.TryGetValue("catalog", out string? target) && target == "ocharo-fashion");
                if (subscription is null)
                {
                    var context = new Dictionary<string, string>
                    {
                        ["catalog"] = "ocharo-fashion",
                        ["sourceGroup"] = group.Id, ["sourceChannel"] = channel.Id,
                    };
                    if (channel.EventFamily is { } family)
                        context["eventFamily"] = family;
                    subscription = Value(await delivery.SubscribeAsync(new()
                    {
                        TenantId = tenant.Id, ContainerId = container.Id, PipelineId = pipeline.Id,
                        FilterKey = OcharoFashionFilter.FilterKey, Schema = PostEnvelopeConstants.EventSchema,
                        Context = context, Backfill = BackfillMode.Live,
                    }, cancellationToken));
                    subscriptions.Add(subscription);
                }
                rows.Add(new(profile.Key, seed, source.Id, pipeline.Id, subscription.Id)
                {
                    GroupId = group.Id, ChannelId = channel.Id, EventFamily = channel.EventFamily,
                });
            }
            return Result<OcharoSetup>.Ok(new(tenant.Id, container.Id, rows));
        }
        catch (ProvisioningFailure failure) { return Result<OcharoSetup>.Fail(failure.Error); }
        finally { SetupGate.Release(); }
    }

    /// <summary>Reads what setup owns on this target, writing nothing: the tenant, its container and each selected channel's
    /// pipeline with its schedules.</summary>
    /// <remarks>A tenant or container setup has not created answers <c>NotFound</c>; a channel without a pipeline reads
    /// as a row with no pipeline.</remarks>
    public async Task<Result<OcharoStatus>> ReadAsync(IReadOnlyList<string>? groupIds = null, string tenantSlug = DefaultTenantSlug,
        CancellationToken cancellationToken = default)
    {
        OcharoSourceRegistry catalog = OcharoSourceRegistry.Current;
        var selection = groupIds?.ToHashSet(StringComparer.Ordinal);
        if (selection is not null && (selection.Count == 0 || selection.Count != groupIds!.Count
            || selection.Any(id => !catalog.Groups.Any(group => group.Id == id))))
            return Result<OcharoStatus>.Fail(AppErrorFactory.Validation("Select one or more distinct publisher IDs from the Ocharo source registry."));
        try
        {
            TenantModel? tenant = Value(await registry.ListTenantsAsync(cancellationToken)).SingleOrDefault(item => item.Slug == tenantSlug);
            ContainerModel? container = tenant is null ? null
                : Value(await registry.ListContainersAsync(tenant.Id, cancellationToken)).SingleOrDefault(item => item.ExternalRef == "ocharo-events");
            if (tenant is null || container is null)
                return Result<OcharoStatus>.Fail(AppErrorFactory.NotFound($"Tenant {tenantSlug} has no Ocharo container yet; run ocharo setup first."));
            var pipelines = Value(await registry.ListPipelinesAsync(container.Id, cancellationToken));
            var channels = catalog.Routes(includeInstagram: true, selection)
                .Select(route => new OcharoChannelStatus(route.Group.Id, route.Channel.Id,
                    pipelines.FirstOrDefault(pipeline => pipeline.Scope.Url == route.Channel.PipelineUrl)))
                .ToList();
            return Result<OcharoStatus>.Ok(new(tenant.Id, container.Id, channels));
        }
        catch (ProvisioningFailure failure) { return Result<OcharoStatus>.Fail(failure.Error); }
    }

    /// <summary>Returns an existing pipeline's full cadence with the registry's enabled rows switched on, or null when every
    /// such row already runs.</summary>
    /// <remarks>
    ///   - an operator-tuned row keeps its interval, cron, window and time zone; only its switch changes
    ///   - a registry row the pipeline lacks is added with the registry's default cadence
    ///   - every other row is carried unchanged, since an update disables the kinds it omits
    /// </remarks>
    private static List<ScheduleDeclaration>? EnabledCadence(PipelineModel pipeline, IReadOnlyList<ScheduleDeclaration> registry)
    {
        var wanted = registry.Where(row => row.Enabled).ToDictionary(row => row.Kind);
        bool changed = false;
        var cadence = new List<ScheduleDeclaration>();
        foreach (ScheduleModel row in pipeline.Schedules)
        {
            bool enable = wanted.Remove(row.Kind) && !row.Enabled;
            changed |= enable;
            cadence.Add(new()
            {
                Kind = row.Kind, Mode = row.Mode, Every = row.Every, MaxEvery = row.MaxEvery, Cron = row.Cron,
                TimeZone = row.TimeZone, Window = row.Window, Enabled = row.Enabled || enable,
            });
        }
        cadence.AddRange(wanted.Values);
        return changed || wanted.Count > 0 ? cadence : null;
    }

    /// <summary>Names a source as the console shows it: a site by its host without <c>www.</c>, any other family by its
    /// handle.</summary>
    private static string DisplayName(SourceModel source) =>
        source.Family == SourceFamily.Site && source.Handle.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? source.Handle[4..] : source.Handle;

    private static T Value<T>(Result<T> result) where T : notnull => result switch
    {
        Result<T>.Success success => success.Value,
        Result<T>.Failure failure => throw new ProvisioningFailure(failure.Error),
        _ => throw new InvalidOperationException("Unexpected registry result."),
    };

    private static Result<OcharoSetup> Invalid(string message) => Result<OcharoSetup>.Fail(AppErrorFactory.Validation(message));
    private static Result<OcharoSetup> Conflict(string message) => Result<OcharoSetup>.Fail(AppErrorFactory.Conflict(message));

    private sealed class ProvisioningFailure(AppError error) : Exception(error.Message)
    {
        internal AppError Error { get; } = error;
    }
}
