namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.BirBir;

internal static class BirBirListingUrls
{
    private const string RussianPath = "/ru/tashkent/cat/nedvizhimost/arenda/kvartiry";
    private const string UzbekPath = "/uz/toshkent/cat/kochmas-mulk/ijara/kvartiralar";

    internal static bool IsCatalog(Uri url) => SupportedHost(url)
        && url.AbsolutePath.TrimEnd('/') is RussianPath or UzbekPath;

    internal static string? ListingId(Uri url)
    {
        if (!SupportedHost(url))
            return null;

        var path = url.AbsolutePath.TrimEnd('/');
        var offerPath = path.StartsWith(RussianPath + "/o/", StringComparison.Ordinal) ? RussianPath + "/o/" : UzbekPath + "/o/";
        if (!path.StartsWith(offerPath, StringComparison.Ordinal))
            return null;

        var slug = path[offerPath.Length..];
        int separator = slug.LastIndexOf('-');
        if (separator < 1 || slug.Contains('/'))
            return null;

        string id = slug[(separator + 1)..];
        return id.Length is > 0 and <= BirBirSourceFields.IdLimit && id.All(char.IsAsciiDigit) ? id : null;
    }

    internal static bool IsImage(string? url) => url is { Length: > 0 and <= BirBirSourceFields.UrlLimit }
        && Uri.TryCreate(url, UriKind.Absolute, out var image)
        && SafeHttps(image) && image.IdnHost is "img.birbir.uz" or "cdn-img.birbir.uz";

    private static bool SupportedHost(Uri url) => SafeHttps(url) && url.IdnHost == BirBirSourceAdapter.Host
        && url.Query.Length == 0 && url.Fragment.Length == 0 && url.AbsoluteUri.Length <= BirBirSourceFields.UrlLimit;

    private static bool SafeHttps(Uri url) => url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps
        && url.UserInfo.Length == 0 && url.IsDefaultPort;
}
