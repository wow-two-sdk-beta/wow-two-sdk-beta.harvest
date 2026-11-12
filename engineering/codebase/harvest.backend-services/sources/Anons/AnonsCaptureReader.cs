using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using Capture = WoW.Two.Sdk.Beta.Be.Harvest.Readers.Capture;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Anons;

/// <summary>Reads ANONS's public event pages and discovers links present in listing HTML.</summary>
public sealed partial class AnonsCaptureReader : ICaptureReader
{
    /// <inheritdoc />
    public string Key => "anons-uz";

    /// <inheritdoc />
    public bool CanRead(FetchedDocument document) => AnonsUrls.IsHost(document.FinalUrl.IdnHost)
        && (AnonsUrls.IsArticle(document.FinalUrl) || AnonsUrls.IsListing(document.FinalUrl))
        && (document.ContentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true
            || document.ContentType is null && Encoding.UTF8.GetString(document.Body.Span).TrimStart().StartsWith('<'));

    /// <inheritdoc />
    public async Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanRead(document))
            return Result<CaptureReadResult>.Fail(AppErrorFactory.SerializationFailed("The document is not public ANONS HTML."));
        IDocument html = await new HtmlParser().ParseDocumentAsync(Decode(document), cancellationToken);
        if (!AnonsUrls.IsArticle(document.FinalUrl))
            return Result<CaptureReadResult>.Ok(new CaptureReadResult { Discovered = Discover(html, document.FinalUrl), Complete = false });

        JsonElement? original = AnonsStructuredData.Event(html);
        string title = Clean(html.QuerySelector(".section-container h1, article h1, h1")?.TextContent
            ?? AnonsStructuredData.String(original, "name"));
        if (title.Length == 0)
            return Result<CaptureReadResult>.Fail(AppErrorFactory.SerializationFailed("The ANONS event has no readable title."));
        Uri? canonical = AnonsUrls.Resolve(document.FinalUrl, html.QuerySelector("link[rel='canonical']")?.GetAttribute("href"));
        if (canonical is null || !AnonsUrls.IsArticle(canonical))
            canonical = document.FinalUrl;
        string identity = AnonsUrls.Identity(canonical);
        string locale = html.DocumentElement?.GetAttribute("lang") ?? AnonsStructuredData.String(original, "inLanguage")
            ?? canonical.AbsolutePath.Split('/')[1];
        IElement? content = AnonsContent.Read(html);
        string text = Text(content);
        if (text.Length == 0)
            text = Clean(AnonsStructuredData.String(original, "description"));
        IReadOnlyDictionary<string, string> fields = Fields(html);
        string[] tags = html.QuerySelectorAll(".section-container .tags .tag").Select(element => Clean(element.TextContent))
            .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        JsonElement projected = AnonsEventProjector.Project(original, identity, title, text, locale, fields, tags);
        JsonElement metadata = JsonSerializer.SerializeToElement(new
        {
            reader = "page", title, text, source = "anons-uz", canonical = identity, anons = projected.GetProperty("anons"),
        });
        IReadOnlyList<JsonElement> structured = [projected, metadata];
        IReadOnlyList<CaptureLink> links = Links(content, document.FinalUrl);
        IReadOnlyList<CaptureMedia> media = Images(html, content, original, document.FinalUrl);
        DateTimeOffset? published = Timestamp(html.QuerySelector("meta[property='article:published_time']")?.GetAttribute("content"));
        var capture = new Capture
        {
            ExternalKey = identity, Url = identity, OriginKey = identity,
            RequestUrl = document.RequestUrl.AbsoluteUri, FinalUrl = document.FinalUrl.AbsoluteUri,
            HttpStatus = document.StatusCode, CapturedAtUtc = document.FetchedAtUtc, ContentType = document.ContentType,
            Body = document.Body, Locale = locale, PublishedAtUtc = published,
            Structured = structured, Links = links, Media = media,
            ContentProjection = JsonSerializer.Serialize(new { canonical = identity, title, text, locale, structured, links, media }),
            Warnings = projected.TryGetProperty("startDate", out _) ? [] : ["event_date_unknown"],
        };
        return Result<CaptureReadResult>.Ok(new CaptureReadResult { Captures = [capture], Complete = false });
    }

    private static IReadOnlyList<DiscoveredUrl> Discover(IDocument html, Uri basis)
    {
        var targets = new Dictionary<string, DiscoveredUrl>(StringComparer.Ordinal);
        string current = AnonsUrls.Listing(basis);
        foreach (IElement element in html.QuerySelectorAll("a[href], [data-url]"))
        {
            string? raw = element.GetAttribute("href") ?? element.GetAttribute("data-url");
            Uri? url = AnonsUrls.Resolve(basis, AnonsUrls.WebUrl(basis, raw));
            if (url is null)
                continue;
            if (url.AbsolutePath.Contains("/fragment/", StringComparison.Ordinal) && !EventSection(element))
                continue;
            if (AnonsUrls.IsArticle(url))
            {
                string identity = AnonsUrls.Identity(url);
                targets.TryAdd(identity, new DiscoveredUrl { Url = identity });
            }
            else if (AnonsUrls.IsListing(url))
            {
                string listing = AnonsUrls.Listing(url);
                if (listing != current)
                    targets.TryAdd(listing, new DiscoveredUrl { Url = listing, IsListing = true });
            }
        }
        return targets.Values.ToArray();
    }

    private static bool EventSection(IElement element)
    {
        string heading = Clean(element.Closest("section")?.QuerySelector("h2, h3")?.TextContent);
        return heading.Equals("События", StringComparison.OrdinalIgnoreCase)
            || heading.Equals("Events", StringComparison.OrdinalIgnoreCase)
            || heading.Equals("Tadbirlar", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> Fields(IDocument html)
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (IElement element in html.QuerySelectorAll(".section-container .section-more__content"))
        {
            string label = Clean(element.QuerySelector("b")?.TextContent).TrimEnd(':');
            string value = Text(element.QuerySelector("span"));
            if (label.Length > 0 && value.Length > 0)
                fields.TryAdd(label, value);
        }
        return fields;
    }

    private static IReadOnlyList<CaptureLink> Links(IElement? content, Uri basis) => content?.QuerySelectorAll("a[href]")
        .Select(element => new { Element = element, Url = AnonsUrls.WebUrl(basis, element.GetAttribute("href")) })
        .Where(item => item.Url is not null).Select(item => new CaptureLink
        {
            Url = item.Url!, Text = Clean(item.Element.TextContent), Rel = item.Element.GetAttribute("rel"),
        }).DistinctBy(item => item.Url).ToArray() ?? [];

    private static IReadOnlyList<CaptureMedia> Images(IDocument html, IElement? content, JsonElement? original, Uri basis)
    {
        IHtmlCollection<IElement> primary = html.QuerySelectorAll(".section-container .section-image img[data-full]");
        if (primary.Length == 0)
            primary = html.QuerySelectorAll(".section-container .section-image img[src]");
        var raw = primary.Select(element =>
            element.GetAttribute("data-full") ?? element.GetAttribute("src")).ToList();
        if (content is not null)
            raw.AddRange(content.QuerySelectorAll("img[src]").Select(element => element.GetAttribute("src")));
        raw.Add(html.QuerySelector("meta[property='og:image']")?.GetAttribute("content"));
        if (original is { } node && node.TryGetProperty("image", out JsonElement image))
            raw.Add(image.ValueKind == JsonValueKind.String ? image.GetString()
                : AnonsStructuredData.String(image, "url") ?? AnonsStructuredData.String(image, "contentUrl"));
        return raw.Select(value => AnonsUrls.WebUrl(basis, value)).Where(value => value is not null).Cast<string>()
            .Distinct(StringComparer.Ordinal).Select((url, index) => new CaptureMedia { Url = url, Kind = MediaKind.Image, Position = index }).ToArray();
    }

    private static string Decode(FetchedDocument document)
    {
        Encoding encoding = Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(document.Charset))
        {
            try { encoding = Encoding.GetEncoding(document.Charset.Trim('"', '\'')); }
            catch (ArgumentException) { /* Unknown character sets fall back to UTF-8. */ }
        }
        return encoding.GetString(document.Body.Span).TrimStart('\uFEFF');
    }

    private static DateTimeOffset? Timestamp(string? raw) => DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
        DateTimeStyles.None, out DateTimeOffset timestamp) ? timestamp.ToUniversalTime() : null;

    private static string Text(INode? node) => node is null ? string.Empty : Clean(string.Join(' ', TextNodes(node)));

    private static IEnumerable<string> TextNodes(INode node)
    {
        if (node is IText text)
            yield return text.Data;
        else if (node is not IElement element || element.LocalName is not ("script" or "style" or "noscript" or "template"))
            foreach (INode child in node.ChildNodes)
                foreach (string value in TextNodes(child))
                    yield return value;
    }

    private static string Clean(string? value) => Space().Replace(value ?? string.Empty, " ").Trim().Normalize(NormalizationForm.FormC);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Space();
}
