namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Refers to what happened to one post or URL — the third of the three outcome levels.</summary>
public enum ItemOutcome
{
    /// <summary>Seen for the first time.</summary>
    New,

    /// <summary>Its content hash changed.</summary>
    Changed,

    /// <summary>Its content hash did not change.</summary>
    Unchanged,

    /// <summary>Its link went gone.</summary>
    Gone,

    /// <summary>It moved to another URL.</summary>
    Moved,

    /// <summary>Its link turned suspect.</summary>
    Suspect,

    /// <summary>It came back after it was gone.</summary>
    Relisted,

    /// <summary>Its source is unavailable, so nothing is inferred.</summary>
    SourceUnavailable,

    /// <summary>Model tiers were skipped; the capture still delivers.</summary>
    ExtractionSkipped,

    /// <summary>Its media was too large to store.</summary>
    MediaTooBig,
}
