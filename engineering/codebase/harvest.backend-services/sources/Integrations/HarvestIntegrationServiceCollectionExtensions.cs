using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Captcha;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Proxy;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Routing;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Sessions;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations;

/// <summary>Registers private integrations explicitly after the generic engine's fetching services.</summary>
public static class HarvestIntegrationServiceCollectionExtensions
{
    /// <summary>Registers configurable route/session and CAPTCHA clients; all settings start disabled.</summary>
    public static OptionsBuilder<HarvestIntegrationOptions> AddHarvestIntegrations(
        this IServiceCollection services,
        Action<HarvestIntegrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        OptionsBuilder<HarvestIntegrationOptions> options = services.AddOptions<HarvestIntegrationOptions>()
            .PostConfigure(settings => settings.LoadProxyProfiles())
            .Validate(settings => settings.HasValidProxyConfiguration(),
                "HarvestIntegrations requires valid, unambiguous proxy profiles and route references.");
        if (configure is not null)
            options.Configure(configure);
        services.AddHttpClient(TwoCaptchaSolver.ProviderClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
            .AddSafeOutboundHttp();
        services.TryAddSingleton<AccountSessionPool>();
        services.TryAddSingleton<IOutboundAddressResolver, SystemOutboundAddressResolver>();
        services.TryAddSingleton<IProxyHttpClientFactory, GuardedProxyClientFactory>();
        services.AddOutboundProxyBroker();
        services.TryAddSingleton<ICaptchaSolver, TwoCaptchaSolver>();
        services.Replace(ServiceDescriptor.Singleton<IFetchTransportFactory, ConfiguredFetchTransportFactory>());
        return options;
    }
}
