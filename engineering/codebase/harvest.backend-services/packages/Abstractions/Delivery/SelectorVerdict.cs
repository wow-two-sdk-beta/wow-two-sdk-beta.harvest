namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Refers to what a selector says about one revision of a post.</summary>
public enum SelectorVerdict
{
    /// <summary>The revision passes the selector.</summary>
    Match,

    /// <summary>The revision fails the selector.</summary>
    NoMatch,

    /// <summary>The selector cannot judge yet: the post has no Layer 1 tags, or no entity type while one is required.</summary>
    Pending,
}
