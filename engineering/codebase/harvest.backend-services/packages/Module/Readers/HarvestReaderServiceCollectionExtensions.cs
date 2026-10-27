using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;

/// <summary>Registers the public family readers and the generic direct-fetch adapters.</summary>
public static class HarvestReaderServiceCollectionExtensions
{
    /// <summary>Registers sitemap, feed, calendar, JSON-LD event and plain-page readers in precedence order.</summary>
    /// <param name="services">The host's services; fetching is registered separately.</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddHarvestReaders(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICaptureReader, SitemapCaptureReader>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICaptureReader, FeedCaptureReader>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICaptureReader, IcsCaptureReader>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICaptureReader, JsonLdEventReader>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICaptureReader, PageCaptureReader>());
        foreach (var family in new[] { SourceFamily.Site, SourceFamily.Structured, SourceFamily.Page, SourceFamily.Feed, SourceFamily.Link })
        {
            var adapter = new GenericSourceAdapter(family);
            services.AddSingleton<ISourceAdapter>(adapter);
            foreach (var step in new[] { "enumerate", "detail", "check" })
                services.AddScoped<IStepBroker>(provider => new GenericStepBroker(adapter.Key, step,
                    provider.GetRequiredService<IPageFetcher>(), provider.GetServices<ICaptureReader>()));
        }
        return services;
    }
}
