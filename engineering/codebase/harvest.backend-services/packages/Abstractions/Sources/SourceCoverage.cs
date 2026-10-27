namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Refers to how much of a source its pipelines see.</summary>
public enum SourceCoverage
{
    /// <summary>Every post: a sitemap, a full channel.</summary>
    Whole,

    /// <summary>Only what pipelines discover through their filters.</summary>
    Filtered,

    /// <summary>One submitted URL.</summary>
    Single,
}
