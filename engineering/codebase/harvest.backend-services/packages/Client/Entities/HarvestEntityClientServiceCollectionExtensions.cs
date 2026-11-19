using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Entities;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Entities;

/// <summary>Registers the entity reads and change feed under the configured Harvest service endpoint.</summary>
public static class HarvestEntityClientServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IEntitiesBroker"/> using the settings and credential configured by
    /// <c>AddHarvestClient</c>, which runs first. The caller needs <c>posts:read</c>.</summary>
    /// <param name="services">The consuming host's service collection.</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddHarvestEntityClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpClient<IEntitiesBroker, ApiEntitiesBroker>(HarvestHttp.Configure).WithCredentials();
        return services;
    }
}
