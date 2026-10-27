using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Runs;

/// <summary>Registers execution control explicitly under the configured Harvest service endpoint.</summary>
public static class HarvestRunClientServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IRunBroker"/> using settings configured by <c>AddHarvestClient</c>.</summary>
    /// <param name="services">The consuming host's service collection.</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddHarvestRunClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpClient<IRunBroker, ApiRunBroker>((provider, http) =>
        {
            var options = provider.GetRequiredService<IOptions<HarvestClientOptions>>().Value;
            if (options.BaseAddress is not { IsAbsoluteUri: true })
                throw new InvalidOperationException("HarvestClientOptions.BaseAddress must be an absolute URL.");
            http.BaseAddress = new Uri(options.BaseAddress.ToString().TrimEnd('/') + "/");
            if (!string.IsNullOrEmpty(options.ApiKey))
                http.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, options.ApiKey);
        });
        return services;
    }
}
