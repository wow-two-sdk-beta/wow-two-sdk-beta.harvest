namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Refers to what a link in a post leads to, by host table.</summary>
public enum PostLinkKind
{
    /// <summary>A ticket seller.</summary>
    Ticket,

    /// <summary>A social profile or post.</summary>
    Social,

    /// <summary>A messenger chat.</summary>
    Messenger,

    /// <summary>A map.</summary>
    Map,

    /// <summary>A website.</summary>
    Site,

    /// <summary>Anything else.</summary>
    Other,
}
