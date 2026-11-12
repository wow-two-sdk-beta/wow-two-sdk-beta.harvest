using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Olx;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

/// <summary>Composes private marketplace sources over Harvest without subscribing them to the Ocharo event feed.</summary>
public static class MarketplaceServiceCollectionExtensions
{
    /// <summary>Registers public OLX catalog, detail and availability readers; registration performs no I/O.</summary>
    public static IServiceCollection AddHarvestListingSources(this IServiceCollection services)
    {
        services.AddHarvestWebBroker<OlxSourceAdapter, OlxCaptureReader>();
        return services;
    }
}
