using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Registers the private official-API adapter; registration performs no network calls.</summary>
public static class InstagramServiceCollectionExtensions
{
    /// <summary>Registers an isolated Meta client and source broker; defaults allow no requests.</summary>
    /// <param name="services">The host services.</param>
    public static OptionsBuilder<InstagramOptions> AddHarvestInstagram(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(InstagramBusinessDiscoveryClient.ClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddSafeOutboundHttp(policy => policy.AllowedHosts.Add("graph.facebook.com"));
        services.TryAddSingleton<IInstagramMediaClient, InstagramBusinessDiscoveryClient>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISourceAdapter, InstagramSourceAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepBroker, InstagramStepBroker>());
        return services.AddOptions<InstagramOptions>();
    }
}
