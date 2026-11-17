using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Readers;

/// <summary>Represents an address a reader found for later steps, such as a sitemap entry.</summary>
public sealed record DiscoveredUrl
{
    /// <summary>Gets the absolute URL.</summary>
    public required string Url { get; init; }

    /// <summary>Gets the source's stable identifier when the catalog exposes it.</summary>
    public string? ExternalKey { get; init; }

    /// <summary>Gets the title visible in the catalog.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the source publication time when the catalog gives an exact instant.</summary>
    public DateTimeOffset? PublishedAtUtc { get; init; }

    /// <summary>Gets versioned catalog fields a source opts to preserve.</summary>
    public JsonElement? NativeFields { get; init; }

    /// <summary>Gets when the source says the address last changed (UTC); trusted per source.</summary>
    public DateTimeOffset? LastModifiedUtc { get; init; }

    /// <summary>Gets whether the address is itself a listing to read, such as a child sitemap.</summary>
    public bool IsListing { get; init; }
}
