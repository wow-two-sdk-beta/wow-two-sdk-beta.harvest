using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Domtut;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Olx;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

/// <summary>Composes private marketplace sources over Harvest without subscribing them to the Ocharo event feed.</summary>
public static class MarketplaceServiceCollectionExtensions
{
    /// <summary>Registers public OLX and Domtut catalog, detail and availability readers, the listing delivery filter and the profile
    /// provisioner; registration performs no I/O and creates no rows.</summary>
    public static IServiceCollection AddHarvestListingSources(this IServiceCollection services)
    {
        services.AddHarvestWebBroker<OlxSourceAdapter, OlxCaptureReader>();
        services.AddHarvestWebBroker<DomtutSourceAdapter, DomtutCaptureReader>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDeliveryPostFilter, MarketplaceListingFilter>());
        services.TryAddScoped<MarketplaceProvisioner>();
        return services;
    }
}
