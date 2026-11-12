using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Afisha;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Anons;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Caex;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Craftfair;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Uzexpocentre;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

/// <summary>Composes private source parsers over the public Harvest engine.</summary>
public static class OcharoServiceCollectionExtensions
{
    /// <summary>Registers source brokers and the Ocharo delivery predicate; registration performs no I/O.</summary>
    public static IServiceCollection AddOcharoEventSources(this IServiceCollection services)
    {
        services.AddHarvestWebBroker<AfishaSourceAdapter, AfishaCaptureReader>();
        services.AddHarvestWebBroker<AnonsSourceAdapter, AnonsCaptureReader>();
        services.AddHarvestWebBroker<CraftfairSourceAdapter, CraftfairCaptureReader>();
        services.AddHarvestWebBroker<CaexSourceAdapter, CaexCaptureReader>();
        services.AddHarvestWebBroker<UzexpocentreSourceAdapter, UzexpocentreCaptureReader>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDeliveryPostFilter, OcharoFashionFilter>());
        services.TryAddScoped<OcharoSourceProvisioner>();
        return services;
    }
}
