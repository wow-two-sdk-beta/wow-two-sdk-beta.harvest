using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

/// <summary>Registers the module's capture and post store.</summary>
public static class HarvestPostsServiceCollectionExtensions
{
    /// <summary>Adds post resolution and reversible merge operations to an existing Harvest module.</summary>
    public static IServiceCollection AddHarvestPosts(this IServiceCollection services)
    {
        services.TryAddScoped<IPostBroker, ModulePostBroker>();
        return services;
    }
}
