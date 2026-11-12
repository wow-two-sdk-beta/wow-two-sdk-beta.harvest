using System.Globalization;
using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Olx;

/// <summary>Restricts OLX navigation to public routes and preserves the exact saved search across pagination.</summary>
internal static partial class OlxUrls
{
    internal static bool IsHost(string host)
    {
        return host.Equals("olx.uz", StringComparison.OrdinalIgnoreCase)
            || host.Equals("www.olx.uz", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsPublic(Uri uri)
    {
        return uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && IsHost(uri.IdnHost)
            && (AdKey(uri) is not null || CatalogPath().IsMatch(uri.AbsolutePath));
    }

    internal static string? AdKey(Uri uri)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || !IsHost(uri.IdnHost)) return null;
        Match match = AdPath().Match(uri.AbsolutePath);
        return match.Success ? match.Groups["id"].Value : null;
    }

    internal static Uri? Resolve(Uri basis, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(basis, raw, out Uri? uri) || !IsPublic(uri)) return null;
        return new UriBuilder(uri) { Fragment = string.Empty }.Uri;
    }

    internal static string DetailUrl(Uri uri)
    {
        return new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty }.Uri.AbsoluteUri;
    }

    internal static Uri? NextPage(Uri current, string? raw)
    {
        Uri? next = Resolve(current, raw);
        if (next is null || AdKey(next) is not null || next.IdnHost != current.IdnHost
            || next.AbsolutePath != current.AbsolutePath) return null;
        List<KeyValuePair<string, string>> before = Parameters(current.Query);
        List<KeyValuePair<string, string>> after = Parameters(next.Query);
        if (!Page(after, out int nextPage) || !Page(before, out int currentPage) || nextPage <= currentPage) return null;
        var sourceFilters = before.Where(pair => pair.Key != "page" && !Tracking(pair.Key)).ToList();
        foreach (IGrouping<string, KeyValuePair<string, string>> group in after.Where(pair => pair.Key != "page" && !Tracking(pair.Key)).GroupBy(pair => pair.Key))
        {
            if (!group.Select(pair => pair.Value).Order(StringComparer.Ordinal)
                .SequenceEqual(sourceFilters.Where(pair => pair.Key == group.Key).Select(pair => pair.Value).Order(StringComparer.Ordinal)))
                return null;
        }
        string query = string.Join('&', sourceFilters.Append(new("page", nextPage.ToString(CultureInfo.InvariantCulture)))
            .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        return new UriBuilder(current) { Query = query, Fragment = string.Empty }.Uri;
    }

    private static bool Page(IReadOnlyList<KeyValuePair<string, string>> values, out int page)
    {
        string[] pages = values.Where(pair => pair.Key == "page").Select(pair => pair.Value).ToArray();
        page = 1;
        return pages.Length == 0 || pages.Length == 1
            && int.TryParse(pages[0], NumberStyles.None, CultureInfo.InvariantCulture, out page) && page is >= 1 and <= 10000;
    }

    private static List<KeyValuePair<string, string>> Parameters(string query)
    {
        return query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('=', 2))
            .Select(parts => new KeyValuePair<string, string>(Decode(parts[0]), parts.Length > 1 ? Decode(parts[1]) : string.Empty)).ToList();
    }

    private static string Decode(string value)
    {
        return Uri.UnescapeDataString(value.Replace('+', ' '));
    }

    private static bool Tracking(string key)
    {
        return key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || key is "reason" or "fbclid" or "gclid";
    }

    [GeneratedRegex(@"^/(?:oz/|ru/)?d/(?:oz/|ru/)?(?:obyavlenie|elon)/[^/]*-ID(?<id>[a-zA-Z0-9]+)\.html$", RegexOptions.CultureInvariant)]
    private static partial Regex AdPath();

    [GeneratedRegex(@"^/(?:oz/|ru/)?(?:$|(?:nedvizhimost|transport|elektronika|rabota|uslugi|dom-i-sad|detskiy-mir|moda-i-stil|hobbi-otdyh-i-sport|zhivotnye|otdam-darom|obmen-darom|biznes-i-uslugi|list|obyavleniya)(?:/.*)?$|q-[^/]+/?$)", RegexOptions.CultureInvariant)]
    private static partial Regex CatalogPath();
}
