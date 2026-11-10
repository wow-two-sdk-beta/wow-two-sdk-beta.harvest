using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;

/// <summary>Registers the module's read side of the console's Sources area.</summary>
public static class HarvestSourcesServiceCollectionExtensions
{
    /// <summary>Adds <see cref="ISourceOverviewBroker"/> to an existing Harvest module; it reads the run, post and delivery
    /// tables whether or not this host registers their brokers.</summary>
    public static IServiceCollection AddHarvestSources(this IServiceCollection services)
    {
        services.TryAddScoped<ISourceOverviewBroker, ModuleSourceOverviewBroker>();
        return services;
    }
}
