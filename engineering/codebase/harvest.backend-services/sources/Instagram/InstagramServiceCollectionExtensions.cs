using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Integrations.Instagram;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Registers the private official-API adapter; registration performs no network calls.</summary>
public static class InstagramServiceCollectionExtensions
{
    /// <summary>Registers an isolated Meta client and source broker; only saved, explicitly run pipelines collect posts.</summary>
    /// <param name="services">The host services.</param>
    public static OptionsBuilder<InstagramOptions> AddHarvestInstagram(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddInstagramIntegration(provider => new InstagramAccessTokenService(provider.GetRequiredService<IOptionsMonitor<InstagramOptions>>()));
        services.AddOptions<InstagramIntegrationOptions>().Configure<IOptions<InstagramOptions>>((target, source) =>
        {
            // Unconfigured connections must not prevent unrelated registries from starting.
            InstagramOptions configured = InstagramConfiguration.IsValid(source.Value) ? source.Value : new();
            target.BaseAddress = new Uri(configured.BaseAddress);
        });
        services.TryAddTransient<IInstagramMediaBroker, InstagramMediaBroker>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISourceAdapter, InstagramSourceAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStepBroker, InstagramStepBroker>());
        return services.AddOptions<InstagramOptions>();
    }
}
