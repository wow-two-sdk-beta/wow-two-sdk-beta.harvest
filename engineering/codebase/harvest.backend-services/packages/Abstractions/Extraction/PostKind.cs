namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Refers to what a post is; dedup never merges two kinds.</summary>
public enum PostKind
{
    /// <summary>An event.</summary>
    Event,

    /// <summary>A marketplace listing.</summary>
    Listing,

    /// <summary>An offer.</summary>
    Offer,

    /// <summary>News.</summary>
    News,

    /// <summary>A promotion.</summary>
    Promo,

    /// <summary>A discussion.</summary>
    Discussion,

    /// <summary>Anything else.</summary>
    Other,
}
