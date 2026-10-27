namespace WoW.Two.Sdk.Beta.Be.Harvest.Posts;

/// <summary>Refers to a post's rollup over its verifiable, non-retired links.</summary>
public enum PostState
{
    /// <summary>At least one link is live or relisted, or none can be checked.</summary>
    Live,

    /// <summary>No link is live and at least one is suspect.</summary>
    Suspect,

    /// <summary>Every verifiable link is gone.</summary>
    Gone,
}
