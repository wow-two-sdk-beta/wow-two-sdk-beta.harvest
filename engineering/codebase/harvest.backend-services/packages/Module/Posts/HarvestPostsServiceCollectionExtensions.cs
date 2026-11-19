using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

/// <summary>Registers the module's capture and post store.</summary>
public static class HarvestPostsServiceCollectionExtensions
{
    /// <summary>Adds post resolution and reversible merge operations to an existing Harvest module.</summary>
    public static IServiceCollection AddHarvestPosts(this IServiceCollection services)
    {
        services.TryAddScoped<IPostBroker, ModulePostBroker>();
        services.TryAddScoped<ModulePostBroker>();
        services.TryAddSingleton<PostTagsGate>();
        services.TryAddScoped<PostExtractionGate>();
        services.TryAddScoped<IPostProcessingBroker, ModulePostProcessingBroker>();
        return services;
    }
}
