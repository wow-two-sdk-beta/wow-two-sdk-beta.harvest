namespace WoW.Two.Sdk.Beta.Be.Harvest.Posts;

/// <summary>Refers to one link's state; it moves only on authoritative negatives from an available source.</summary>
public enum LinkState
{
    /// <summary>The link answers.</summary>
    Live,

    /// <summary>A weak negative: absent from a complete enumeration, a soft 404, a vanished node.</summary>
    Suspect,

    /// <summary>A strong negative, confirmed.</summary>
    Gone,

    /// <summary>Seen again after it was gone.</summary>
    Relisted,
}
