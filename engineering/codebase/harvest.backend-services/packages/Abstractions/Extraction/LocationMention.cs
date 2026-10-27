namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents a place a post mentions, as raw text; resolving it is the consumer's.</summary>
public sealed record LocationMention
{
    /// <summary>Gets the mention as written.</summary>
    public required string Text { get; init; }

    /// <summary>Gets what the mention names.</summary>
    public required LocationKind Kind { get; init; }

    /// <summary>Gets coordinates the source gave for it.</summary>
    public GeoPoint? Geo { get; init; }
}
