using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client;

/// <summary>Registers the Harvest HTTP client (Forms 2 and 3).</summary>
public static class HarvestClientServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IRegistrationBroker"/> over the running Harvest service.</summary>
    /// <remarks>The credential is chosen here, from the settings <paramref name="configure"/> writes: an API key, or an
    /// Identity client. <c>AddHarvestDataClient</c> and <c>AddHarvestRunClient</c>, called after this, take the same
    /// one.</remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Sets the service's root URL and the API key or the Identity client.</param>
    /// <returns>The HTTP client builder, for resilience handlers or a test server's handler.</returns>
    public static IHttpClientBuilder AddHarvestClient(this IServiceCollection services, Action<HarvestClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var probe = new HarvestClientOptions();
        configure(probe);
        services.AddSingleton(new HarvestClientRegistration(probe.Identity is not null));
        services.AddOptions<HarvestClientOptions>()
            .Configure(configure)
            .Validate(options => options.BaseAddress is { IsAbsoluteUri: true }, "HarvestClientOptions.BaseAddress must be an absolute URL.")
            .Validate(options => string.IsNullOrEmpty(options.ApiKey) || options.Identity is null, "HarvestClientOptions: set ApiKey or Identity, not both.")
            .Validate(options => options.Identity is null or { TokenEndpoint.IsAbsoluteUri: true }, "HarvestClientOptions.Identity.TokenEndpoint must be an absolute URL.")
            .Validate(options => options.Identity is null || !string.IsNullOrWhiteSpace(options.Identity.ClientId), "HarvestClientOptions.Identity.ClientId is required.")
            .Validate(
                options => options.Identity is null
                    || string.IsNullOrWhiteSpace(options.Identity.ClientSecret) != string.IsNullOrWhiteSpace(options.Identity.ClientPrivateKey),
                "HarvestClientOptions.Identity: set ClientSecret or ClientPrivateKey, exactly one.");

        return services.AddHttpClient<IRegistrationBroker, ApiRegistrationBroker>(HarvestHttp.Configure).WithCredentials();
    }
}
