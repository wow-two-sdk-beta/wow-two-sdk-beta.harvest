namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents where a post is about.</summary>
public sealed record PostLocation
{
    /// <summary>Gets the places the post mentions.</summary>
    public IReadOnlyList<LocationMention> Mentions { get; init; } = [];

    /// <summary>Gets coordinates the source gave for the post.</summary>
    public GeoPoint? Geo { get; init; }
}
