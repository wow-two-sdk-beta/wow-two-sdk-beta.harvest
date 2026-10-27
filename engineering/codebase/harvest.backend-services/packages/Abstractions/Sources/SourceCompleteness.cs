namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Refers to whether a source's enumeration can prove a post is absent.</summary>
public enum SourceCompleteness
{
    /// <summary>An enumeration lists every live post, so absence from a complete pass is a weak negative.</summary>
    Complete,

    /// <summary>An enumeration lists a window (a feed, the newest page, search results); absence proves nothing.</summary>
    Window,

    /// <summary>The source has no enumeration.</summary>
    None,
}
