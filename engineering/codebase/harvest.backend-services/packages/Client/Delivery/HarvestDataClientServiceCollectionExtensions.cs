using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Delivery;

/// <summary>Registers the post and delivery HTTP forms under the Harvest client's configured endpoint.</summary>
public static class HarvestDataClientServiceCollectionExtensions
{
    /// <summary>Adds post and delivery brokers using the settings registered by <c>AddHarvestClient</c>.</summary>
    public static IServiceCollection AddHarvestDataClient(this IServiceCollection services)
    {
        services.AddHttpClient<IPostBroker, ApiPostBroker>(Configure);
        services.AddHttpClient<IDeliveryBroker, ApiDeliveryBroker>(Configure);
        return services;
    }

    private static void Configure(IServiceProvider provider, HttpClient http)
    {
        var options = provider.GetRequiredService<IOptions<HarvestClientOptions>>().Value;
        if (options.BaseAddress is not { IsAbsoluteUri: true }) throw new InvalidOperationException("HarvestClientOptions.BaseAddress must be an absolute URL.");
        http.BaseAddress = new Uri(options.BaseAddress.ToString().TrimEnd('/') + "/");
        if (!string.IsNullOrEmpty(options.ApiKey)) http.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, options.ApiKey);
    }
}
