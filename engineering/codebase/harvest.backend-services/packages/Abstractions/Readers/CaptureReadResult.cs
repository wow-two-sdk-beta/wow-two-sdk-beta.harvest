namespace WoW.Two.Sdk.Beta.Be.Harvest.Readers;

/// <summary>Represents what a reader made of a document: the posts it holds and the addresses it points to.</summary>
public sealed record CaptureReadResult
{
    /// <summary>Gets the posts the document holds: one for a page, one per item for a feed.</summary>
    public IReadOnlyList<Capture> Captures { get; init; } = [];

    /// <summary>Gets the addresses the document points to, such as sitemap entries.</summary>
    public IReadOnlyList<DiscoveredUrl> Discovered { get; init; } = [];

    /// <summary>Gets whether the document listed everything it covers, so absence from it is a weak negative.</summary>
    public bool Complete { get; init; }
}
