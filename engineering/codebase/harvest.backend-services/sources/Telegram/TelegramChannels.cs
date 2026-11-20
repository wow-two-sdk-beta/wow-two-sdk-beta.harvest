using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Telegram;

/// <summary>Names a public Telegram channel the way the registry, the preview page and the permalinks each spell it.</summary>
/// <remarks>The registry stores a channel's handle as <c>tg:{username}</c>; the preview page is
/// <c>https://t.me/s/{username}</c> and the page before a message id adds <c>?before={id}</c>. Every channel shares the
/// host <see cref="Host"/>, so all of them queue behind one polite lane.</remarks>
public static partial class TelegramChannels
{
    /// <summary>Holds the host every Telegram source shares: its pacing, its backoff and its health.</summary>
    public const string Host = "t.me";

    /// <summary>Returns the registry handle of a channel, <c>tg:{username}</c>.</summary>
    /// <param name="username">The channel's lowercase public username.</param>
    public static string Handle(string username) => SourceConstants.TelegramHandlePrefix + username;

    /// <summary>Returns the public preview page of a channel's newest messages.</summary>
    /// <param name="username">The channel's lowercase public username.</param>
    public static string PreviewUrl(string username) => $"https://{Host}/s/{username}";

    /// <summary>Returns the preview page of the messages older than a message id.</summary>
    /// <param name="username">The channel's lowercase public username.</param>
    /// <param name="before">The smallest message id the previous page showed.</param>
    public static string PreviewUrl(string username, long before) => $"{PreviewUrl(username)}?before={before}";

    /// <summary>Returns the public permalink of one message, the credit link every item carries.</summary>
    /// <param name="username">The channel's lowercase public username.</param>
    /// <param name="id">The message id.</param>
    public static string Permalink(string username, long id) => $"https://{Host}/{username}/{id}";

    /// <summary>Reads the username out of a source handle in its registry form.</summary>
    /// <param name="handle">A source handle, <c>tg:{username}</c>.</param>
    /// <param name="username">The lowercase username when the handle is one.</param>
    public static bool TryReadHandle(string? handle, out string username)
    {
        username = string.Empty;
        if (handle is null || !handle.StartsWith(SourceConstants.TelegramHandlePrefix, StringComparison.Ordinal)
            || !Username().IsMatch(handle[SourceConstants.TelegramHandlePrefix.Length..]))
            return false;
        username = handle[SourceConstants.TelegramHandlePrefix.Length..];
        return true;
    }

    /// <summary>Reads a preview page address: <c>https://t.me/s/{username}</c>, optionally with <c>?before={id}</c> and nothing else.</summary>
    /// <param name="url">The address to read.</param>
    /// <param name="username">The lowercase username when the address is a preview page.</param>
    /// <param name="before">The message id the page lies before, or null for the newest page.</param>
    public static bool TryReadPreview(Uri? url, out string username, out long? before)
    {
        username = string.Empty;
        before = null;
        if (url is null || !url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort || url.UserInfo.Length > 0
            || url.Fragment.Length > 0 || !url.IdnHost.Equals(Host, StringComparison.OrdinalIgnoreCase)
            || PreviewPath().Match(url.AbsolutePath) is not { Success: true } path)
            return false;
        string name = path.Groups["name"].Value.ToLowerInvariant();
        if (!Username().IsMatch(name))
            return false;
        if (url.Query.Length > 0)
        {
            if (BeforeQuery().Match(url.Query) is not { Success: true } query || !long.TryParse(query.Groups["id"].Value, out long id) || id < 1)
                return false;
            before = id;
        }
        username = name;
        return true;
    }

    /// <summary>Reads a registry seed: the newest preview page of one channel, with no <c>before</c> cursor.</summary>
    /// <param name="seed">The pipeline URL.</param>
    /// <param name="username">The lowercase username when the seed is a channel's preview page.</param>
    public static bool TryReadSeed(string? seed, out string username)
    {
        username = string.Empty;
        return Uri.TryCreate(seed, UriKind.Absolute, out Uri? uri) && TryReadPreview(uri, out username, out long? before) && before is null;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{3,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex Username();

    [GeneratedRegex("^/s/(?<name>[A-Za-z][A-Za-z0-9_]{3,31})/?$", RegexOptions.CultureInvariant)]
    private static partial Regex PreviewPath();

    [GeneratedRegex("^\\?before=(?<id>[0-9]{1,12})$", RegexOptions.CultureInvariant)]
    private static partial Regex BeforeQuery();
}
