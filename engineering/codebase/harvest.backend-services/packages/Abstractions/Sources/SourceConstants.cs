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

    /// <summary>Gets the seeded retention classes.</summary>
    public static IReadOnlyList<string> RetentionClasses { get; } = ["standard", "youtube", "meta", "x", "telegram", "intake"];

    /// <summary>Gets the seeded deletion-check policies: a direct fetch with age bands, and no checks.</summary>
    public static IReadOnlyList<string> CheckPolicies { get; } = ["web-direct", "none"];
}
