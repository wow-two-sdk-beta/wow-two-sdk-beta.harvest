namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Reads and writes the instance's record of which catalog entries are turned on, for the setup that provisions sources.</summary>
/// <remarks>A provisioner calls it after a successful setup: it links the sources to their entry and writes the entry's record, as
/// <c>ocharo setup</c> and <c>marketplace setup</c> do. The record belongs to the instance, not to a tenant or container.</remarks>
public interface ISourceCatalogRecords
{
    /// <summary>Lists the keys of the entries the instance turned off.</summary>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    Task<IReadOnlySet<string>> OffKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>Links sources to an entry and turns the entry on: stamps the key on every source that carries none, then writes an
    /// <c>on</c> record changed by <c>setup</c> when the entry has none, or, with <paramref name="overrideOff"/>, when it is off.
    /// An entry that is already on keeps its record.</summary>
    /// <param name="entryKey">The entry's key.</param>
    /// <param name="sourceIds">The sources the setup created or found.</param>
    /// <param name="overrideOff">Whether an explicit request for this entry turns it on even when it was off.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    Task RecordSetUpAsync(string entryKey, IReadOnlyCollection<Guid> sourceIds, bool overrideOff, CancellationToken cancellationToken = default);
}
