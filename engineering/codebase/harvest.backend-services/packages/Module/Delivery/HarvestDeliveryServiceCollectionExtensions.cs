using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Registers the module's subscription and pull delivery store.</summary>
public static class HarvestDeliveryServiceCollectionExtensions
{
    /// <summary>Adds pull subscriptions to an existing Harvest module.</summary>
    public static IServiceCollection AddHarvestDelivery(this IServiceCollection services)
    {
        services.TryAddScoped<IDeliveryBroker, ModuleDeliveryBroker>();
        return services;
    }
}
