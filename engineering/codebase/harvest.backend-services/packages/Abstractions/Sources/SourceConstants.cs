namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Holds the limits and seeded vocabularies of source registration.</summary>
public static class SourceConstants
{
    /// <summary>Holds the longest handle.</summary>
    public const int MaxHandleLength = 255;

    /// <summary>Holds the authority a source gets unless it states one.</summary>
    public const int DefaultAuthority = 50;

    /// <summary>Holds the retention class a source gets unless it states one.</summary>
    public const string StandardRetentionClass = "standard";

    /// <summary>Holds the prefix of a Telegram handle.</summary>
    public const string TelegramHandlePrefix = "tg:";

    /// <summary>Holds the longest source group description.</summary>
    public const int MaxGroupDescriptionLength = 1000;

    /// <summary>Holds the shortest gap between two requests to a host an operator may set, in milliseconds.</summary>
    public const int MinReadingDelayMs = 1000;

    /// <summary>Holds the longest gap between two requests to a host an operator may set, in milliseconds.</summary>
    public const int MaxReadingDelayMs = 60000;

    /// <summary>Holds the gap between two requests to a host that has no stored pace, in milliseconds.</summary>
    public const int DefaultReadingDelayMs = 2000;

    /// <summary>Holds the UTC days a source overview counts new posts over, today included.</summary>
    public const int OverviewDays = 14;

    /// <summary>Holds the most posts a source's post list returns unless the caller asks for fewer.</summary>
    public const int DefaultPostsLimit = 200;

    /// <summary>Holds the most posts a source's post list may return.</summary>
    public const int MaxPostsLimit = 500;

    /// <summary>Gets the seeded retention classes.</summary>
    public static IReadOnlyList<string> RetentionClasses { get; } = ["standard", "youtube", "meta", "x", "telegram", "intake"];

    /// <summary>Gets the seeded deletion-check policies: a direct fetch with age bands, and no checks.</summary>
    public static IReadOnlyList<string> CheckPolicies { get; } = ["web-direct", "none"];
}
