using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Declares one catalog of registered sources an instance may turn on: the entries the image ships, how to set one up and
/// which stored sources already belong to which.</summary>
/// <remarks>A catalog is code and data of the image, never of one instance: its entries are the same everywhere, and the
/// instance's own database records which are turned on (<c>harvest_catalog_sources</c>). Entries and keys perform no network I/O.</remarks>
public interface ISourceCatalog
{
    /// <summary>Gets the catalog's key, the first segment of its entries' keys: <c>ocharo</c> or <c>marketplace</c>.</summary>
    string Key { get; }

    /// <summary>Lists every entry of the catalog, in the order the console shows them.</summary>
    IReadOnlyList<CatalogEntry> Entries();

    /// <summary>Provisions the sources of one entry that has none yet, with schedules off and model processing off.</summary>
    /// <param name="entryKey">The entry's key.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The sources the entry covers after the call; a failure carries the reason, and the caller records nothing.</returns>
    Task<Result<CatalogSetUp>> SetUpAsync(string entryKey, CancellationToken cancellationToken = default);

    /// <summary>Finds the stored sources that belong to this catalog's entries, by family and handle in the containers the
    /// catalog's setup uses; it reads and writes nothing else.</summary>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>One link per source and entry; a source no entry names is left out.</returns>
    Task<IReadOnlyList<CatalogSourceLink>> FindSourcesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Represents one entry of a catalog: the unit an instance turns on or off.</summary>
public sealed record CatalogEntry
{
    /// <summary>Gets the entry's key: <c>ocharo.{groupId}</c> or <c>marketplace.{siteKey}</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the key of the catalog the entry belongs to.</summary>
    public required string Catalog { get; init; }

    /// <summary>Gets the name the console shows: a publisher or a site.</summary>
    public required string Name { get; init; }

    /// <summary>Gets a short description, when the catalog has one.</summary>
    public string? Description { get; init; }

    /// <summary>Gets what the entry collects.</summary>
    public required CatalogKind Kind { get; init; }

    /// <summary>Gets the hosts the entry's sources read, one per family and handle.</summary>
    public required IReadOnlyList<CatalogHost> Hosts { get; init; }

    /// <summary>Gets how many channels or searches turn on and off with the entry.</summary>
    public required int Channels { get; init; }
}

/// <summary>Refers to what a catalog entry collects.</summary>
public enum CatalogKind
{
    /// <summary>Events, such as an event publisher's listings.</summary>
    Events,

    /// <summary>Marketplace listings, such as rental ads.</summary>
    Listings,
}

/// <summary>Represents a host a catalog entry reads: the family and handle its source row carries.</summary>
/// <param name="Family">The kind of origin.</param>
/// <param name="Handle">The source's normalized identity: a host, or an account username.</param>
public sealed record CatalogHost(SourceFamily Family, string Handle);

/// <summary>Represents the sources one entry covers after its set-up.</summary>
/// <param name="SourceIds">The sources, one per host the set-up created or found.</param>
public sealed record CatalogSetUp(IReadOnlyList<Guid> SourceIds);

/// <summary>Represents a stored source that belongs to a catalog entry.</summary>
/// <param name="EntryKey">The entry's key.</param>
/// <param name="SourceId">The stored source.</param>
public sealed record CatalogSourceLink(string EntryKey, Guid SourceId);
