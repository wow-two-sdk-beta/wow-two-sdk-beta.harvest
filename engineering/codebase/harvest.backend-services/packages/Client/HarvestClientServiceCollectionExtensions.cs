using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client;

/// <summary>Registers the Harvest HTTP client (Forms 2 and 3).</summary>
public static class HarvestClientServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IRegistrationBroker"/> over the running Harvest service.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Sets the service's root URL and the API key.</param>
    /// <returns>The HTTP client builder, for resilience handlers or a test server's handler.</returns>
    public static IHttpClientBuilder AddHarvestClient(this IServiceCollection services, Action<HarvestClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<HarvestClientOptions>()
            .Configure(configure)
            .Validate(options => options.BaseAddress is { IsAbsoluteUri: true }, "HarvestClientOptions.BaseAddress must be an absolute URL.");

        return services.AddHttpClient<IRegistrationBroker, ApiRegistrationBroker>((provider, http) =>
        {
            var options = provider.GetRequiredService<IOptions<HarvestClientOptions>>().Value;
            var root = options.BaseAddress!.ToString();
            http.BaseAddress = new Uri(root.EndsWith('/') ? root : root + "/");
            if (!string.IsNullOrEmpty(options.ApiKey))
                http.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, options.ApiKey);
        });
    }
}
