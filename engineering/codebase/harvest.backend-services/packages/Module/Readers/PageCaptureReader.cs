using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;

/// <summary>Reads ordinary HTML into a text capture with links, media and OpenGraph metadata.</summary>
public sealed class PageCaptureReader : ICaptureReader
{
    /// <inheritdoc />
    public string Key => "page";

    /// <inheritdoc />
    public bool CanRead(FetchedDocument document) => document.ContentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true
        || ReaderSupport.Decode(document).TrimStart().StartsWith('<') && !ReaderSupport.Decode(document).TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanRead(document)) return Task.FromResult(ReaderSupport.Invalid("HTML"));
        var html = ReaderSupport.Html(ReaderSupport.Decode(document));
        string? Meta(string property) => html.QuerySelectorAll("meta")
            .FirstOrDefault(element => element.GetAttribute("property")?.Equals(property, StringComparison.OrdinalIgnoreCase) == true
                || element.GetAttribute("name")?.Equals(property, StringComparison.OrdinalIgnoreCase) == true)?.GetAttribute("content");
        var canonical = ReaderSupport.Url(document.FinalUrl, html.QuerySelector("link[rel='canonical']")?.GetAttribute("href"));
        var url = canonical ?? document.FinalUrl.AbsoluteUri;
        var title = ReaderSupport.Text(Meta("og:title") ?? html.QuerySelector("h1")?.TextContent ?? html.Title);
        var hadScripts = html.QuerySelector("script[src]") is not null;
        foreach (var element in html.QuerySelectorAll("script,style,noscript,template,nav,aside")) element.Remove();
        var text = ReaderSupport.ElementText(html.QuerySelector("article,main") ?? html.Body);
        if (text.Length == 0) text = ReaderSupport.Text(Meta("og:description") ?? Meta("description"));
        var links = html.QuerySelectorAll("a[href]").Select(element => new
        {
            Url = ReaderSupport.Url(document.FinalUrl, element.GetAttribute("href")),
            Text = ReaderSupport.Text(element.TextContent),
            Rel = element.GetAttribute("rel"),
        }).Where(link => link.Url is not null).DistinctBy(link => link.Url).Select(link => new CaptureLink
        {
            Url = link.Url!, Text = link.Text, Rel = link.Rel,
        }).ToList();
        var media = html.QuerySelectorAll("img[src],video[src],video source[src]").Select(element => new
        {
            Url = ReaderSupport.Url(document.FinalUrl, element.GetAttribute("src")),
            Kind = element.LocalName is "video" or "source" ? MediaKind.Video : MediaKind.Image,
        }).Where(item => item.Url is not null).DistinctBy(item => item.Url).Select((item, index) => new CaptureMedia
        {
            Url = item.Url!, Kind = item.Kind, Position = index,
        }).ToList();
        if (ReaderSupport.Url(document.FinalUrl, Meta("og:image")) is { } cover && media.All(item => item.Url != cover))
            media.Insert(0, new CaptureMedia { Url = cover, Kind = MediaKind.Image });
        var structured = JsonSerializer.SerializeToElement(new
        {
            reader = "page", title, text, description = Meta("og:description") ?? Meta("description"),
            author = Meta("author"), published = Meta("article:published_time"),
            contacts = html.QuerySelectorAll("a[href^='tel:'],a[href^='mailto:']").Select(element =>
                element.GetAttribute("href")![(element.GetAttribute("href")!.IndexOf(':') + 1)..]).ToArray(),
        });
        var projection = JsonSerializer.Serialize(new { title, text, media = media.Select(item => item.Url), links = links.Select(item => item.Url) });
        var capture = ReaderSupport.Capture(document, url, url, projection, [structured], canonical,
            html.DocumentElement?.GetAttribute("lang"), ReaderSupport.Timestamp(Meta("article:published_time")), media, links,
            warnings: text.Length == 0 && hadScripts ? ["render_required"] : []);
        return Task.FromResult(Result<CaptureReadResult>.Ok(new() { Captures = [capture] }));
    }
}
