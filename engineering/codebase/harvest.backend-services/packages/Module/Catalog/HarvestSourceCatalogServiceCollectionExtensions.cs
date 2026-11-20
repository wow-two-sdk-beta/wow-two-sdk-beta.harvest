using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Catalog;

/// <summary>Registers the module's source catalog.</summary>
public static class HarvestSourceCatalogServiceCollectionExtensions
{
    /// <summary>Adds the instance's catalog record (<see cref="ISourceCatalogRecords"/>), its broker and the boot reconcile to an existing
    /// Harvest module; the catalogs themselves (<see cref="ISourceCatalog"/>) come from the host, and with none registered the list is
    /// empty. <c>AddHarvestSources</c> calls it.</summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddHarvestSourceCatalog(this IServiceCollection services)
    {
        services.TryAddScoped<SourceCatalogStore>();
        services.TryAddScoped<ISourceCatalogRecords>(provider => provider.GetRequiredService<SourceCatalogStore>());
        services.TryAddScoped<SourceCatalogBroker>();
        services.TryAddScoped<SourceCatalogReconciler>();
        return services;
    }
}
