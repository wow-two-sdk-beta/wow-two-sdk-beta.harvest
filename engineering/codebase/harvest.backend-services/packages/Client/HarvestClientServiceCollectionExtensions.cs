using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Greetings;
using WoW.Two.Sdk.Beta.Be.Harvest.Greetings;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client;

/// <summary>Registers the Harvest HTTP client (Forms 2 and 3).</summary>
public static class HarvestClientServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IGreetingsBroker"/> over the running Harvest service.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Sets the service's root URL.</param>
    /// <returns>The HTTP client builder, for resilience handlers or a test server's handler.</returns>
    public static IHttpClientBuilder AddHarvestClient(this IServiceCollection services, Action<HarvestClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<HarvestClientOptions>()
            .Configure(configure)
            .Validate(options => options.BaseAddress is { IsAbsoluteUri: true }, "HarvestClientOptions.BaseAddress must be an absolute URL.");

        return services.AddHttpClient<IGreetingsBroker, ApiGreetingsBroker>((provider, http) =>
        {
            var root = provider.GetRequiredService<IOptions<HarvestClientOptions>>().Value.BaseAddress!.ToString();
            http.BaseAddress = new Uri(root.EndsWith('/') ? root : root + "/");
        });
    }
}
