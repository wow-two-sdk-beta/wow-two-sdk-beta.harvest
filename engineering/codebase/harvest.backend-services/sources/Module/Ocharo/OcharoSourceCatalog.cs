using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

/// <summary>Offers the Ocharo registry's publishers to the instance's source catalog: one entry per publisher group with at least one
/// channel in <c>harvest</c> or <c>provider</c> mode, keyed <c>ocharo.{groupId}</c>.</summary>
/// <remarks>A group with no collectible channel is no entry, since nothing of it could run. An entry's sources land in the owning
/// tenant's <c>ocharo-events</c> container, as <c>ocharo setup</c> puts them, one per host (an Instagram channel's host is its username).
/// Listing the entries performs no I/O.</remarks>
/// <param name="registry">The registry the entry's sources are found in.</param>
/// <param name="provisioner">The setup a turned-on <c>new</c> entry runs.</param>
public sealed class OcharoSourceCatalog(IRegistrationBroker registry, OcharoSourceProvisioner provisioner) : ISourceCatalog
{
    /// <summary>Holds the catalog's key.</summary>
    public const string CatalogKey = "ocharo";

    private const string Prefix = CatalogKey + ".";

    private static readonly IReadOnlyList<CatalogEntry> All = Build();

    /// <summary>Returns the catalog key of a publisher group.</summary>
    /// <param name="groupId">The registry's publisher ID.</param>
    public static string EntryKey(string groupId) => Prefix + groupId;

    /// <inheritdoc />
    public string Key => CatalogKey;

    /// <inheritdoc />
    public IReadOnlyList<CatalogEntry> Entries() => All;

    /// <inheritdoc />
    public async Task<Result<CatalogSetUp>> SetUpAsync(string entryKey, CancellationToken cancellationToken = default)
    {
        if (All.SingleOrDefault(entry => entry.Key == entryKey) is null)
            return Result<CatalogSetUp>.Fail(AppErrorFactory.NotFound($"No Ocharo catalog entry has the key '{entryKey}'."));
        var setup = await provisioner.EnsureAsync(enableSchedules: false, cancellationToken, includeInstagram: true,
            groupIds: [entryKey[Prefix.Length..]], modelProcessing: null, recordCatalog: false);
        return setup is Result<OcharoSetup>.Success { Value: var value }
            ? Result<CatalogSetUp>.Ok(new CatalogSetUp([.. value.Sources.Select(source => source.SourceId).Distinct()]))
            : Result<CatalogSetUp>.Fail(((Result<OcharoSetup>.Failure)setup).Error);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogSourceLink>> FindSourcesAsync(CancellationToken cancellationToken = default)
    {
        if (await registry.ListTenantsAsync(cancellationToken) is not Result<IReadOnlyList<WoW.Two.Sdk.Beta.Be.Harvest.Tenants.TenantModel>>.Success { Value: var tenants }
            || tenants.SingleOrDefault(item => item.Slug == OcharoSourceProvisioner.DefaultTenantSlug) is not { } tenant
            || await registry.ListContainersAsync(tenant.Id, cancellationToken) is not Result<IReadOnlyList<WoW.Two.Sdk.Beta.Be.Harvest.Containers.ContainerModel>>.Success { Value: var containers }
            || containers.SingleOrDefault(item => item.ExternalRef == "ocharo-events") is not { } container
            || await registry.ListSourcesAsync(container.Id, cancellationToken) is not Result<IReadOnlyList<SourceModel>>.Success { Value: var sources })
            return [];
        var links = new List<CatalogSourceLink>();
        foreach (var entry in All)
            foreach (var host in entry.Hosts)
                foreach (var source in sources.Where(item => item.Family == host.Family && item.Handle == host.Handle))
                    links.Add(new CatalogSourceLink(entry.Key, source.Id));
        return links;
    }

    private static IReadOnlyList<CatalogEntry> Build()
    {
        OcharoSourceRegistry registry = OcharoSourceRegistry.Current;
        var routes = registry.Routes(includeInstagram: true, registry.Groups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal))
            .ToLookup(route => route.Group.Id, StringComparer.Ordinal);
        var entries = new List<CatalogEntry>();
        foreach (OcharoSourceGroup group in registry.Groups.OrderBy(group => group.Rank))
        {
            var collectible = routes[group.Id].ToArray();
            if (collectible.Length == 0)
                continue;
            entries.Add(new CatalogEntry
            {
                Key = EntryKey(group.Id), Catalog = CatalogKey, Name = group.Name, Description = group.Coverage, Kind = CatalogKind.Events,
                Hosts = [.. collectible.Select(route => new CatalogHost(route.Profile.Family, HandleOf(route.Profile.Family, route.Channel.PipelineUrl)))
                    .Distinct()],
                Channels = collectible.Length,
            });
        }
        return entries;
    }

    /// <summary>Names the host a channel's source carries: the seed's host, or an Instagram account's username.</summary>
    private static string HandleOf(SourceFamily family, string seed) =>
        family == SourceFamily.Instagram && InstagramPublishers.TryReadProfile(seed, out string username, out _) ? username : new Uri(seed).IdnHost;
}
