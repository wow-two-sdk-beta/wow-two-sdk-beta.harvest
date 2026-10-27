namespace WoW.Two.Sdk.Beta.Be.Harvest.Posts;

/// <summary>Refers to how a link shows its post.</summary>
public enum LinkRole
{
    /// <summary>The post's own page or message in its primary locale.</summary>
    Primary,

    /// <summary>The same post in another locale, declared by structure.</summary>
    Alternate,

    /// <summary>A repost or forward.</summary>
    Copy,

    /// <summary>A member of an album or a thread.</summary>
    Part,

    /// <summary>A reply or comment by someone else; kept out of resolution, dedup and state.</summary>
    Reply,
}
