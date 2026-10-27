namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Refers to the kind of origin a source is, which picks the adapters that can read it.</summary>
public enum SourceFamily
{
    /// <summary>A plain web page.</summary>
    Page,

    /// <summary>A page carrying structured data such as JSON-LD.</summary>
    Structured,

    /// <summary>A whole site enumerated from its sitemap.</summary>
    Site,

    /// <summary>A site that needs a rendering browser.</summary>
    Render,

    /// <summary>A provider API.</summary>
    Api,

    /// <summary>An RSS, Atom or ICS feed.</summary>
    Feed,

    /// <summary>A submitted link read as a card.</summary>
    Link,

    /// <summary>A marketplace of listings, such as OLX.</summary>
    Listing,

    /// <summary>A Telegram channel or group.</summary>
    Telegram,

    /// <summary>A WhatsApp channel or intake number.</summary>
    Whatsapp,

    /// <summary>An Instagram account or tag.</summary>
    Instagram,

    /// <summary>A Facebook page or group.</summary>
    Facebook,

    /// <summary>A YouTube channel.</summary>
    Youtube,

    /// <summary>A Threads account.</summary>
    Threads,

    /// <summary>An X account or search.</summary>
    X,

    /// <summary>Consented submissions through an intake bot or number.</summary>
    Intake,

    /// <summary>Hand-typed entries.</summary>
    Manual,
}
