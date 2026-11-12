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

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Afisha;

/// <summary>Discovers Afisha's visible category links and reads explicit event data from its public HTML.</summary>
public sealed partial class AfishaCaptureReader : ICaptureReader
{
    private const string ParserVersion = "afisha-html@4";

    /// <inheritdoc />
    public string Key => "afisha-uz";

    /// <inheritdoc />
    public bool CanRead(FetchedDocument document)
    {
        return AfishaUrls.IsHost(document.FinalUrl.IdnHost)
            && (document.ContentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true
                || document.ContentType is null && Encoding.UTF8.GetString(document.Body.Span).TrimStart().StartsWith('<'))
            && (AfishaUrls.IsArticle(document.FinalUrl) || AfishaUrls.IsCategory(document.FinalUrl)
                || document.FinalUrl.AbsolutePath is "/ru" or "/ru/" or "/uz" or "/uz/");
    }

    /// <inheritdoc />
    public async Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanRead(document))
            return Result<CaptureReadResult>.Fail(AppErrorFactory.SerializationFailed("The document is not public Afisha HTML."));
        IDocument html = await new HtmlParser().ParseDocumentAsync(Decode(document), cancellationToken);
        if (!AfishaUrls.IsArticle(document.FinalUrl))
            return Result<CaptureReadResult>.Ok(new CaptureReadResult { Discovered = Discover(html, document.FinalUrl), Complete = false });

        JsonElement? material = AfishaEmbeddedData.Material(html);
        JsonElement? original = AfishaEmbeddedData.Event(html);
        string title = Clean(AfishaEmbeddedData.String(material, "title") ?? html.QuerySelector("article h1, h1")?.TextContent);
        if (title.Length == 0)
            return Result<CaptureReadResult>.Fail(AppErrorFactory.SerializationFailed("The Afisha article has no readable title."));
        Uri? canonical = AfishaUrls.Resolve(document.FinalUrl, html.QuerySelector("link[rel='canonical']")?.GetAttribute("href"));
        if (canonical is null || !AfishaUrls.IsArticle(canonical))
            canonical = document.FinalUrl;
        string identity = AfishaUrls.Identity(canonical);
        string locale = AfishaEmbeddedData.String(material, "locale") ?? html.DocumentElement?.GetAttribute("lang")
            ?? (canonical.AbsolutePath.StartsWith("/uz/", StringComparison.Ordinal) ? "uz" : "ru");
        string? body = AfishaEmbeddedData.Content(html, material);
        IElement? content = Content(html, body);
        string text = BodyText(content);
        string intro = Clean(AfishaEmbeddedData.String(material, "intro"));
        if (text.Length == 0)
            text = intro;
        string[] paragraphs = content?.QuerySelectorAll("p,li").Select(element => Text(element)).Where(value => value.Length > 0).ToArray() ?? [];
        var (eventNode, evidence) = AfishaEventProjector.Project(
            original,
            material,
            title,
            text,
            locale,
            identity,
            paragraphs);
        string[] sourceTags = material is { } source && source.TryGetProperty("tags", out JsonElement tags)
            && tags.ValueKind == JsonValueKind.Array
            ? tags.EnumerateArray().Where(tag => tag.ValueKind == JsonValueKind.String)
                .Select(tag => tag.GetString()!).Distinct(StringComparer.Ordinal).ToArray() : [];
        string[] contacts = content?.QuerySelectorAll("a[href]")
            .Select(anchor => anchor.GetAttribute("href")!)
            .Where(href => href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            .Select(href => Uri.UnescapeDataString(href[(href.IndexOf(':') + 1)..].Split('?')[0])).ToArray() ?? [];
        JsonElement metadata = JsonSerializer.SerializeToElement(new
        {
            reader = "page", title, text, afisha = evidence,
            source = "afisha-uz", canonical = identity, sourceTags, contacts,
            sourceFields = new
            {
                schema = "afisha.material.v1", parserVersion = ParserVersion,
                fields = material, contentHtml = body, originalEvent = original,
            },
        });
        IReadOnlyList<JsonElement> structured = eventNode is { } node ? [node, metadata] : [metadata];
        IReadOnlyList<CaptureLink> links = Links(content, document.FinalUrl);
        IReadOnlyList<CaptureMedia> media = Images(html, content, document.FinalUrl);
        // Native counters and Flight reference IDs are snapshot evidence, not semantic content changes.
        string projection = JsonSerializer.Serialize(new
        {
            parserVersion = ParserVersion, canonical = identity, title, text, locale,
            eventNode, evidence, sourceTags, contacts, links, media,
        });
        DateTimeOffset? published = Timestamp(AfishaEmbeddedData.String(material, "publishedAt")
            ?? html.QuerySelector("meta[property='article:published_time']")?.GetAttribute("content")
            ?? html.QuerySelector("article time[datetime]")?.GetAttribute("datetime"));
        var warnings = new List<string>();
        if (body is null && AfishaEmbeddedData.String(material, "content")?.StartsWith('$') == true)
            warnings.Add("source_content_reference_unresolved");
        if (eventNode is { } projected && !projected.TryGetProperty("startDate", out _))
            warnings.Add("event_date_unknown");
        if (evidence.GetProperty("recordKind").GetString() == "article")
            warnings.Add("editorial_or_reportage");
        var capture = new Capture
        {
            ExternalKey = identity, Url = identity, OriginKey = identity,
            RequestUrl = document.RequestUrl.AbsoluteUri, FinalUrl = document.FinalUrl.AbsoluteUri,
            HttpStatus = document.StatusCode, CapturedAtUtc = document.FetchedAtUtc, ContentType = document.ContentType,
            Body = document.Body, ContentProjection = projection, Locale = locale, PublishedAtUtc = published,
            Structured = structured, Links = links, Media = media, Warnings = warnings,
        };
        return Result<CaptureReadResult>.Ok(new CaptureReadResult { Captures = [capture], Complete = false });
    }

    private static IReadOnlyList<DiscoveredUrl> Discover(IDocument html, Uri basis)
    {
        var targets = new Dictionary<string, DiscoveredUrl>(StringComparer.Ordinal);
        foreach (IElement anchor in html.QuerySelectorAll("a[href]"))
        {
            Uri? url = AfishaUrls.Resolve(basis, anchor.GetAttribute("href"));
            if (url is null)
                continue;
            if (AfishaUrls.IsArticle(url))
            {
                string identity = AfishaUrls.Identity(url);
                targets.TryAdd(identity, new DiscoveredUrl { Url = identity });
            }
            else if (AfishaUrls.IsCategory(url) && !string.Equals(url.AbsolutePath.TrimEnd('/'), basis.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal))
            {
                string listing = AfishaUrls.Identity(url);
                targets.TryAdd(listing, new DiscoveredUrl { Url = listing, IsListing = true });
            }
        }
        return targets.Values.ToArray();
    }

    private static IElement? Content(IDocument html, string? body)
    {
        if (body is not null)
            return new HtmlParser().ParseDocument(body).Body;
        IElement? content = html.QuerySelector("article [class*='content_content__'], article [data-content]");
        if (content is not null)
            return content;
        IElement? article = html.QuerySelector("article");
        if (article is null)
            return null;
        var copy = (IElement)article.Clone();
        foreach (IElement element in copy.QuerySelectorAll("script,style,nav,aside,footer,h1,time,[data-schedule],button"))
            element.Remove();
        return copy;
    }

    private static IReadOnlyList<CaptureLink> Links(IElement? content, Uri basis)
    {
        return content?.QuerySelectorAll("a[href]").Select(element => new
        {
            Element = element,
            Url = WebUrl(basis, element.GetAttribute("href")),
        }).Where(item => item.Url is not null).Select(item => new CaptureLink
        {
            Url = item.Url!, Text = Clean(item.Element.TextContent), Rel = item.Element.GetAttribute("rel"),
        }).DistinctBy(item => item.Url).ToArray() ?? [];
    }

    private static IReadOnlyList<CaptureMedia> Images(IDocument html, IElement? content, Uri basis)
    {
        var urls = html.QuerySelectorAll("article img[src]").Select(element => WebUrl(basis, element.GetAttribute("src"))).ToList();
        if (content is not null)
            urls.AddRange(content.QuerySelectorAll("img[src]").Select(element => WebUrl(basis, element.GetAttribute("src"))));
        urls.Add(WebUrl(basis, html.QuerySelector("meta[property='og:image']")?.GetAttribute("content")));
        return urls.Where(url => url is not null).Cast<string>().Distinct(StringComparer.Ordinal)
            .Select((url, index) => new CaptureMedia { Url = url, Kind = MediaKind.Image, Position = index }).ToArray();
    }

    private static string? WebUrl(Uri basis, string? raw)
    {
        if (raw is null || !Uri.TryCreate(basis, raw, out Uri? uri) || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo))
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

    private static DateTimeOffset? Timestamp(string? value)
    {
        return value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset timestamp)
            ? timestamp.ToUniversalTime() : null;
    }

    private static string Text(INode? node)
    {
        if (node is null)
            return string.Empty;
        return Clean(string.Join(' ', TextNodes(node)));
    }

    private static string BodyText(INode? node)
    {
        if (node is null)
            return string.Empty;
        return string.Join('\n', string.Join(' ', BodyParts(node)).Split('\n')
            .Select(Clean).Where(line => line.Length > 0));
    }

    private static IEnumerable<string> BodyParts(INode node)
    {
        if (node is IText text)
            yield return text.Data;
        else if (node is not IElement element || element.LocalName is not ("script" or "style" or "noscript" or "template"))
        {
            bool block = node is IElement blockElement
                && blockElement.LocalName is "p" or "li" or "blockquote" or "h2" or "h3" or "h4" or "br";
            if (block)
                yield return "\n";
            foreach (INode child in node.ChildNodes)
                foreach (string value in BodyParts(child))
                    yield return value;
            if (block)
                yield return "\n";
        }
    }

    private static IEnumerable<string> TextNodes(INode node)
    {
        if (node is IText text)
            yield return text.Data;
        else if (node is not IElement element || element.LocalName is not ("script" or "style" or "noscript" or "template"))
            foreach (INode child in node.ChildNodes)
                foreach (string value in TextNodes(child))
                    yield return value;
    }

    private static string Clean(string? value)
    {
        return Space().Replace(value ?? string.Empty, " ").Trim().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Space();
}
