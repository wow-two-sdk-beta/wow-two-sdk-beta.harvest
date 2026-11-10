using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Delivery;

/// <summary>Registers the post and delivery HTTP forms under the Harvest client's configured endpoint.</summary>
public static class HarvestDataClientServiceCollectionExtensions
{
    /// <summary>Adds post and delivery brokers using the settings and credential registered by <c>AddHarvestClient</c>,
    /// which runs first.</summary>
    public static IServiceCollection AddHarvestDataClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpClient<IPostBroker, ApiPostBroker>(HarvestHttp.Configure).WithCredentials();
        services.AddHttpClient<IDeliveryBroker, ApiDeliveryBroker>(HarvestHttp.Configure).WithCredentials();
        return services;
    }
}
