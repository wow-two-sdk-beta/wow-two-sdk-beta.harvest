using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Afisha;

/// <summary>Restricts discovery and canonical identity to Afisha's public source routes.</summary>
internal static partial class AfishaUrls
{
    internal static IReadOnlyDictionary<string, string> Sections { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["fashion"] = "Fashion",
        ["exhibitions"] = "Exhibitions",
        ["concerts"] = "Concerts",
    };

    internal static bool IsHost(string host)
    {
        return host.Equals("afisha.uz", StringComparison.OrdinalIgnoreCase)
            || host.Equals("www.afisha.uz", StringComparison.OrdinalIgnoreCase);
    }

    internal static Uri? Resolve(Uri basis, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(basis, value.Trim(), out Uri? uri)
            || uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)
            || !IsHost(uri.IdnHost))
            return null;
        return new UriBuilder(uri) { Fragment = string.Empty }.Uri;
    }

    internal static bool IsArticle(Uri uri)
    {
        return IsHost(uri.IdnHost) && ArticlePath().IsMatch(uri.AbsolutePath);
    }

    internal static bool IsCategory(Uri uri)
    {
        if (!IsHost(uri.IdnHost))
            return false;
        Match match = CategoryPath().Match(uri.AbsolutePath);
        return match.Success && Sections.ContainsKey(match.Groups[1].Value);
    }

    internal static string Identity(Uri url)
    {
        return new UriBuilder(url)
        {
            Host = "www.afisha.uz", Scheme = "https", Port = -1, Query = string.Empty, Fragment = string.Empty,
        }.Uri.AbsoluteUri.TrimEnd('/');
    }

    [GeneratedRegex(@"^/(?:ru|uz)/(?:[a-z0-9-]+/)?20\d{2}/\d{2}/\d{2}/[^/]+/?$", RegexOptions.CultureInvariant)]
    private static partial Regex ArticlePath();

    [GeneratedRegex(@"^/(?:ru|uz)/([a-z0-9-]+)/?$", RegexOptions.CultureInvariant)]
    private static partial Regex CategoryPath();
}
