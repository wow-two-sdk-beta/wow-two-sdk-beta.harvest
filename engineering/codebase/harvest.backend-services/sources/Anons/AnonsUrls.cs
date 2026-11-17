using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Anons;

internal static partial class AnonsUrls
{
    internal static bool IsHost(string host) => host.Equals("anons.uz", StringComparison.OrdinalIgnoreCase)
        || host.Equals("www.anons.uz", StringComparison.OrdinalIgnoreCase);

    internal static bool IsArticle(Uri url) => ArticlePath().IsMatch(url.AbsolutePath);

    internal static bool IsListing(Uri url) => ListingPath().IsMatch(url.AbsolutePath);

    /// <summary>Names the locale segment, such as <c>ru</c>; empty outside a locale route.</summary>
    internal static string Locale(Uri url) => url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) is [{ } first, ..]
        ? first : string.Empty;

    internal static bool IsEvents(Uri url) => EventsPath().IsMatch(url.AbsolutePath);

    internal static bool IsFragment(Uri url) => url.AbsolutePath.Contains("/fragment/", StringComparison.Ordinal);

    /// <summary>A calendar day of the events list, <c>/{locale}/events?date=YYYY-MM-DD</c>.</summary>
    internal static bool IsCalendarDay(Uri url) => IsEvents(url) && CalendarQuery().IsMatch(url.Query);

    internal static Uri? Resolve(Uri basis, string? raw)
    {
        return raw is not null && Uri.TryCreate(basis, raw, out Uri? uri) && IsHost(uri.IdnHost)
            && uri.Scheme is "http" or "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 ? uri : null;
    }

    internal static string Identity(Uri url) => new UriBuilder(url)
    {
        Host = "anons.uz", Scheme = "https", Port = -1, Query = string.Empty, Fragment = string.Empty,
    }.Uri.AbsoluteUri.TrimEnd('/');

    internal static string Listing(Uri url) => new UriBuilder(url)
    {
        Host = "anons.uz", Scheme = "https", Port = -1, Fragment = string.Empty,
    }.Uri.AbsoluteUri.TrimEnd('/');

    internal static string? WebUrl(Uri basis, string? raw)
    {
        if (raw is null || !Uri.TryCreate(basis, raw, out Uri? uri) || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length > 0)
            return null;
        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        if (builder.Query.Length > 1)
            builder.Query = string.Join('&', builder.Query[1..].Split('&').Where(part =>
            {
                string key = Uri.UnescapeDataString(part.Split('=', 2)[0]);
                return !key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
                    && !key.Equals("fbclid", StringComparison.OrdinalIgnoreCase)
                    && !key.Equals("gclid", StringComparison.OrdinalIgnoreCase);
            }).Order(StringComparer.Ordinal));
        return builder.Uri.AbsoluteUri;
    }

    [GeneratedRegex(@"^/(?:ru|uz|en)/events/[^/]+/?$", RegexOptions.CultureInvariant)]
    private static partial Regex ArticlePath();

    [GeneratedRegex(@"^/(?:ru|uz|en)(?:/events|/fragment/section/\d+)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex ListingPath();

    [GeneratedRegex(@"^/(?:ru|uz|en)/events/?$", RegexOptions.CultureInvariant)]
    private static partial Regex EventsPath();

    [GeneratedRegex(@"^\?date=20\d{2}-\d{2}-\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CalendarQuery();
}
