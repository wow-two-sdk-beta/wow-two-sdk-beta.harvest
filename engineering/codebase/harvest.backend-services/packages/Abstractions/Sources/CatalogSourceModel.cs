namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents one catalog entry as an instance stands on it: what it is, whether it is turned on and which sources it set up.</summary>
public sealed record CatalogSourceModel
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

    /// <summary>Gets the hosts the entry's sources read.</summary>
    public required IReadOnlyList<CatalogHost> Hosts { get; init; }

    /// <summary>Gets how many channels or searches turn on and off with the entry.</summary>
    public required int Channels { get; init; }

    /// <summary>Gets where the entry stands on this instance.</summary>
    public required CatalogState State { get; init; }

    /// <summary>Gets when the entry's record last changed (UTC); null when the entry has no record.</summary>
    public DateTimeOffset? ChangedAtUtc { get; init; }

    /// <summary>Gets who changed it: an Identity subject, <c>operator-key</c>, <c>setup</c> or <c>existing</c>; null with no record.</summary>
    public string? ChangedBy { get; init; }

    /// <summary>Gets the sources the entry set up on this instance, across containers.</summary>
    public required IReadOnlyList<CatalogSourceRef> Sources { get; init; }
}

/// <summary>Refers to where a catalog entry stands on an instance.</summary>
public enum CatalogState
{
    /// <summary>No record and no source set up: the entry is listed as available.</summary>
    New,

    /// <summary>Turned on: its sources run as their own settings say.</summary>
    On,

    /// <summary>Turned off: its sources never schedule, capture, process or deliver, and nothing is deleted.</summary>
    Off,
}

/// <summary>Refers to one source a catalog entry set up.</summary>
public sealed record CatalogSourceRef
{
    /// <summary>Gets the source.</summary>
    public required Guid SourceId { get; init; }

    /// <summary>Gets the container the source belongs to.</summary>
    public required Guid ContainerId { get; init; }

    /// <summary>Gets the source's display name, else its handle.</summary>
    public required string Name { get; init; }
}

/// <summary>Represents a request to turn a catalog entry on or off.</summary>
public sealed record CatalogSwitchRequest
{
    /// <summary>Gets whether the entry is turned on.</summary>
    public required bool TurnedOn { get; init; }
}
