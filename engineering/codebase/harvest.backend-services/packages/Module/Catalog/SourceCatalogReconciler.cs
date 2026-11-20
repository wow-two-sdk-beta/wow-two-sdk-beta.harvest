using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Catalog;

/// <summary>Keeps existing sources on when the catalog arrives: stamps the key of the entry each source belongs to and records the
/// stamped entries that have no record as <c>on</c>, changed by <c>existing</c>.</summary>
/// <remarks>It never changes an existing record, and a rerun changes nothing.</remarks>
internal sealed class SourceCatalogReconciler(IEnumerable<ISourceCatalog> catalogs, SourceCatalogStore store)
{
    internal async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        foreach (var catalog in catalogs)
        {
            var links = await catalog.FindSourcesAsync(cancellationToken);
            foreach (var entry in links.GroupBy(link => link.EntryKey, StringComparer.Ordinal))
                await store.StampAsync(entry.Key, [.. entry.Select(link => link.SourceId).Distinct()], cancellationToken);
        }
        await store.RecordExistingAsync(cancellationToken);
    }
}

/// <summary>Reconciles the source catalog after the Harvest schema is ready, before the workers start.</summary>
public static class SourceCatalogStartupExtensions
{
    /// <summary>Stamps each catalog's existing sources with their entry's key and writes an <c>on</c> record for each stamped entry with
    /// none; an existing record never changes.</summary>
    /// <param name="services">The built host's services.</param>
    /// <param name="ct">The token that cancels the reconcile.</param>
    public static async Task ReconcileHarvestSourceCatalogAsync(this IServiceProvider services, CancellationToken ct)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SourceCatalogReconciler>().ReconcileAsync(ct);
    }
}
