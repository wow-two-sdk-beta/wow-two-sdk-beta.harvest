using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Runs;

/// <summary>Registers execution control explicitly under the configured Harvest service endpoint.</summary>
public static class HarvestRunClientServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IRunBroker"/> using the settings and credential configured by <c>AddHarvestClient</c>, which runs first.</summary>
    /// <param name="services">The consuming host's service collection.</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddHarvestRunClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpClient<IRunBroker, ApiRunBroker>(HarvestHttp.Configure).WithCredentials();
        return services;
    }
}
