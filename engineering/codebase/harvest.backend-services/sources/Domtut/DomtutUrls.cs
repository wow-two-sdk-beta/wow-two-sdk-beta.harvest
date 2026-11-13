using System.Globalization;
using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Domtut;

/// <summary>Restricts Domtut navigation to its server-rendered catalog and listing routes and preserves the saved
/// catalog filters across pagination.</summary>
internal static partial class DomtutUrls
{
    // Query fragments Domtut's robots.txt disallows (read 2026-11-14); a fetch that robots would refuse backs the whole
    // host off, so the routes are refused here instead.
    private static readonly string[] RobotsQueryFragments =
        ["sort=", "lat=", "height=", "floor=", "heat_system=", "max-price=", "min-price=", "min-area=", "max-area="];

    internal static bool IsHost(string host)
    {
        return host.Equals("domtut.uz", StringComparison.OrdinalIgnoreCase)
            || host.Equals("www.domtut.uz", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsPublic(Uri uri)
    {
        return Safe(uri) && !RobotsQueryFragments.Any(fragment => uri.Query.Contains(fragment, StringComparison.Ordinal))
            && (Slug(uri) is not null || CatalogPath().IsMatch(uri.AbsolutePath));
    }

    internal static string? Slug(Uri uri)
    {
        if (!Safe(uri)) return null;
        Match match = DetailPath().Match(uri.AbsolutePath);
        return match.Success ? match.Groups["slug"].Value.ToLowerInvariant() : null;
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
        if (next is null || Slug(next) is not null || next.IdnHost != current.IdnHost
            || next.AbsolutePath != current.AbsolutePath) return null;
        List<KeyValuePair<string, string>> before = Parameters(current.Query);
        List<KeyValuePair<string, string>> after = Parameters(next.Query);
        if (!Page(after, out int nextPage) || !Page(before, out int currentPage) || nextPage <= currentPage) return null;
        var sourceFilters = before.Where(pair => pair.Key != "p" && !Tracking(pair.Key)).ToList();
        foreach (IGrouping<string, KeyValuePair<string, string>> group in after.Where(pair => pair.Key != "p" && !Tracking(pair.Key)).GroupBy(pair => pair.Key))
        {
            if (!group.Select(pair => pair.Value).Order(StringComparer.Ordinal)
                .SequenceEqual(sourceFilters.Where(pair => pair.Key == group.Key).Select(pair => pair.Value).Order(StringComparer.Ordinal)))
                return null;
        }
        string query = string.Join('&', sourceFilters.Append(new("p", nextPage.ToString(CultureInfo.InvariantCulture)))
            .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        return new UriBuilder(current) { Query = query, Fragment = string.Empty }.Uri;
    }

    private static bool Safe(Uri uri)
    {
        return uri.IsAbsoluteUri && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && IsHost(uri.IdnHost);
    }

    private static bool Page(IReadOnlyList<KeyValuePair<string, string>> values, out int page)
    {
        string[] pages = values.Where(pair => pair.Key == "p").Select(pair => pair.Value).ToArray();
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
        return key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || key is "fbclid" or "gclid" or "yadclid";
    }

    [GeneratedRegex(@"^/(?:uz/|ru/)?nedvizhimost/(?<slug>[a-zA-Z0-9][a-zA-Z0-9_-]*)/?$", RegexOptions.CultureInvariant)]
    private static partial Regex DetailPath();

    // The new-build catalog, its sections (instalments, mortgage, special offers) and the housing-class pages render on the
    // server; /filter/ searches (rent included) are JavaScript shells and stay out.
    [GeneratedRegex(@"^/(?:uz/|ru/)?(?:catalog-nedvijimosty(?:/[a-z0-9_-]+)?|comfort-class|business-class|premium-class)/?$", RegexOptions.CultureInvariant)]
    private static partial Regex CatalogPath();
}
