using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Sources;

/// <summary>Registers the console's Sources reads under the configured Harvest service endpoint.</summary>
public static class HarvestSourceClientServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ISourceOverviewBroker"/> using the settings and credential configured by
    /// <c>AddHarvestClient</c>, which runs first.</summary>
    /// <param name="services">The consuming host's service collection.</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddHarvestSourceClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpClient<ISourceOverviewBroker, ApiSourceOverviewBroker>(HarvestHttp.Configure).WithCredentials();
        return services;
    }
}
