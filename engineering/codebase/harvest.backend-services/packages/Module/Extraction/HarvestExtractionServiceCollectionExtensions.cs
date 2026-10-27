using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;

/// <summary>Registers the deterministic and rules extraction tiers.</summary>
public static class HarvestExtractionServiceCollectionExtensions
{
    /// <summary>Registers the model-free base-envelope extractor; no external capabilities are enabled.</summary>
    /// <param name="services">The host's services.</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddHarvestExtraction(this IServiceCollection services)
    {
        services.TryAddSingleton<IPostExtractor, RulesPostExtractor>();
        return services;
    }
}
