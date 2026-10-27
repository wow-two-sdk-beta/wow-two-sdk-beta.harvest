using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;

/// <summary>Composes a private named-source parser with the public fetching and web-step engine.</summary>
public static class HarvestWebBrokerServiceCollectionExtensions
{
    /// <summary>Registers one source adapter and gives its reader precedence over generic parser fallbacks.</summary>
    public static IServiceCollection AddHarvestWebBroker<TAdapter, TReader>(this IServiceCollection services)
        where TAdapter : class, ISourceAdapter
        where TReader : class, ICaptureReader
    {
        services.TryAddSingleton<TAdapter>();
        services.TryAddSingleton<TReader>();
        services.AddSingleton<ISourceAdapter>(provider => provider.GetRequiredService<TAdapter>());
        foreach (string step in new[] { "enumerate", "detail", "check" })
        {
            services.AddSingleton<IStepBroker>(provider => new GenericStepBroker(
                provider.GetRequiredService<TAdapter>().Key,
                step,
                provider.GetRequiredService<IPageFetcher>(),
                [provider.GetRequiredService<TReader>(), .. provider.GetServices<ICaptureReader>()]));
        }
        return services;
    }
}
