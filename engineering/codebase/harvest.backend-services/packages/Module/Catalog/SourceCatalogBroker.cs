using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Catalog;

/// <summary>Lists the instance's source catalog and turns its entries on and off.</summary>
/// <remarks>The list merges every registered <see cref="ISourceCatalog"/>'s entries with the instance's records and stamped sources; with
/// no catalog registered it is empty. An entry is <c>new</c> with no record and no source set up, <c>on</c> with a record that is on, or
/// with no record and a source set up, and <c>off</c> with a record that is off.</remarks>
/// <param name="catalogs">The catalogs the host registered.</param>
/// <param name="store">The instance's records.</param>
public sealed class SourceCatalogBroker(IEnumerable<ISourceCatalog> catalogs, SourceCatalogStore store)
{
    /// <summary>Lists every registered entry with its state, record and the sources it set up.</summary>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    public async Task<IReadOnlyList<CatalogSourceModel>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entries = catalogs.SelectMany(catalog => catalog.Entries()).ToList();
        if (entries.Count == 0)
            return [];
        var records = await store.ListRecordsAsync(cancellationToken);
        var stamped = await store.StampedSourcesAsync(cancellationToken);
        return [.. entries.Select(entry => Model(entry, records, stamped))];
    }

    /// <summary>Turns an entry on or off.</summary>
    /// <remarks>Turning on a <c>new</c> entry, or an <c>off</c> one that never set anything up, sets it up first through its catalog, with
    /// schedules and model processing off; a failed set-up is a <c>Conflict</c> and the record stays as it was. Turning on an
    /// <c>off</c> entry that has sources sets nothing up: it resumes as it was. Turning off writes the record and nothing else; a
    /// <c>new</c> entry gets an off record and nothing is set up.</remarks>
    /// <param name="key">The entry's key.</param>
    /// <param name="turnedOn">Whether the entry is turned on.</param>
    /// <param name="changedBy">The caller's subject, or <c>operator-key</c>.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The entry as it now stands; <c>NotFound</c> for an unknown key, <c>Conflict</c> for a set-up that failed.</returns>
    public async Task<Result<CatalogSourceModel>> SwitchAsync(string key, bool turnedOn, string changedBy, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(changedBy);
        ISourceCatalog? owner = catalogs.FirstOrDefault(catalog => catalog.Entries().Any(entry => entry.Key == key));
        if (owner is null)
            return Result<CatalogSourceModel>.Fail(AppErrorFactory.NotFound($"No catalog entry has the key '{key}'."));

        var current = (await ListAsync(cancellationToken)).Single(model => model.Key == key);
        if (turnedOn && current.State == CatalogState.On)
            return Result<CatalogSourceModel>.Ok(current);
        if (turnedOn && current.Sources.Count == 0)
        {
            var setUp = await owner.SetUpAsync(key, cancellationToken);
            if (setUp is not Result<CatalogSetUp>.Success { Value: var value })
                return Result<CatalogSourceModel>.Fail(AppErrorFactory.Conflict(((Result<CatalogSetUp>.Failure)setUp).Error.Message));
            await store.StampAsync(key, value.SourceIds, cancellationToken);
        }
        await store.SetAsync(key, turnedOn, changedBy, cancellationToken);
        return Result<CatalogSourceModel>.Ok((await ListAsync(cancellationToken)).Single(model => model.Key == key));
    }

    private static CatalogSourceModel Model(CatalogEntry entry, IReadOnlyDictionary<string, CatalogRecord> records,
        IReadOnlyDictionary<string, IReadOnlyList<CatalogSourceRef>> stamped)
    {
        records.TryGetValue(entry.Key, out var record);
        IReadOnlyList<CatalogSourceRef> sources = stamped.GetValueOrDefault(entry.Key) ?? [];
        return new CatalogSourceModel
        {
            Key = entry.Key, Catalog = entry.Catalog, Name = entry.Name, Description = entry.Description, Kind = entry.Kind,
            Hosts = entry.Hosts, Channels = entry.Channels,
            State = record is null ? sources.Count > 0 ? CatalogState.On : CatalogState.New : record.TurnedOn ? CatalogState.On : CatalogState.Off,
            ChangedAtUtc = record?.ChangedAtUtc, ChangedBy = record?.ChangedBy, Sources = sources,
        };
    }
}
