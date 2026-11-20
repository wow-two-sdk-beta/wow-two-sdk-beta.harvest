using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

/// <summary>Offers the marketplace sites to the instance's source catalog: one entry per distinct site across the reviewed profiles,
/// keyed <c>marketplace.{siteKey}</c>.</summary>
/// <remarks>A site's searches turn on and off with its entry, whichever profile lists them; its sources land in each profile's private
/// container, one per site and intake mode, so a site with a collected and an import-only variant (BirBir) is one entry covering two
/// sources. Listing the entries performs no I/O.</remarks>
/// <param name="registry">The registry the entry's sources are found in.</param>
/// <param name="provisioner">The setup a turned-on <c>new</c> entry runs.</param>
public sealed class MarketplaceSourceCatalog(IRegistrationBroker registry, MarketplaceProvisioner provisioner) : ISourceCatalog
{
    /// <summary>Holds the catalog's key.</summary>
    public const string CatalogKey = "marketplace";

    private const string Prefix = CatalogKey + ".";

    private static readonly IReadOnlyList<CatalogEntry> All = Build();

    /// <summary>Returns the catalog key of a site.</summary>
    /// <param name="siteKey">The site's key.</param>
    public static string EntryKey(string siteKey) => Prefix + siteKey;

    /// <inheritdoc />
    public string Key => CatalogKey;

    /// <inheritdoc />
    public IReadOnlyList<CatalogEntry> Entries() => All;

    /// <inheritdoc />
    public async Task<Result<CatalogSetUp>> SetUpAsync(string entryKey, CancellationToken cancellationToken = default)
    {
        if (All.SingleOrDefault(entry => entry.Key == entryKey) is null)
            return Result<CatalogSetUp>.Fail(AppErrorFactory.NotFound($"No marketplace catalog entry has the key '{entryKey}'."));
        string siteKey = entryKey[Prefix.Length..];
        var sourceIds = new List<Guid>();
        foreach (MarketplaceProfile profile in MarketplaceProfiles.All.Where(profile => profile.Searches.Any(search => search.Site.Key == siteKey)))
        {
            var setup = await provisioner.EnsureAsync(profile, enableSchedules: false, MarketplaceKeyAction.None, allowAi: false, modelProcessing: null,
                cancellationToken, siteKey: siteKey, recordCatalog: false);
            if (setup is not Result<MarketplaceSetup>.Success { Value: var value })
                return Result<CatalogSetUp>.Fail(((Result<MarketplaceSetup>.Failure)setup).Error);
            sourceIds.AddRange(value.Searches.Select(search => search.SourceId));
        }
        return Result<CatalogSetUp>.Ok(new CatalogSetUp([.. sourceIds.Distinct()]));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogSourceLink>> FindSourcesAsync(CancellationToken cancellationToken = default)
    {
        var links = new List<CatalogSourceLink>();
        foreach (MarketplaceProfile profile in MarketplaceProfiles.All)
        {
            if (await registry.ListTenantsAsync(cancellationToken) is not Result<IReadOnlyList<TenantModel>>.Success { Value: var tenants }
                || tenants.SingleOrDefault(item => item.Slug == profile.TenantSlug) is not { } tenant
                || await registry.ListContainersAsync(tenant.Id, cancellationToken) is not Result<IReadOnlyList<ContainerModel>>.Success { Value: var containers }
                || containers.SingleOrDefault(item => item.ExternalRef == profile.ContainerRef) is not { } container
                || await registry.ListSourcesAsync(container.Id, cancellationToken) is not Result<IReadOnlyList<SourceModel>>.Success { Value: var sources })
                continue;
            foreach (MarketplaceSite site in profile.Searches.Select(search => search.Site).Distinct())
                foreach (var source in sources.Where(item => item.Family == MarketplaceProvisioner.FamilyOf(site) && item.Handle == site.Handle))
                    links.Add(new CatalogSourceLink(EntryKey(site.Key), source.Id));
        }
        return links;
    }

    private static IReadOnlyList<CatalogEntry> Build()
    {
        var searches = MarketplaceProfiles.All.SelectMany(profile => profile.Searches).ToArray();
        return [.. searches.GroupBy(search => search.Site.Key, StringComparer.Ordinal).Select(site => new CatalogEntry
        {
            Key = EntryKey(site.Key), Catalog = CatalogKey, Name = site.First().Site.Name, Kind = CatalogKind.Listings,
            Hosts = [.. site.Select(search => new CatalogHost(MarketplaceProvisioner.FamilyOf(search.Site), search.Site.Handle)).Distinct()],
            Channels = site.Count(),
        })];
    }
}
