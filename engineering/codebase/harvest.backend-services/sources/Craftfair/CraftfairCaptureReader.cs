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

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Craftfair;

/// <summary>Reads published event dates and artisan application evidence separately.</summary>
public sealed partial class CraftfairCaptureReader : ICaptureReader
{
    /// <inheritdoc />
    public string Key => "craftfair-uz";

    /// <inheritdoc />
    public bool CanRead(FetchedDocument document) =>
        document.FinalUrl.IdnHost is "craftfair.uz" or "www.craftfair.uz"
        && EventPath().IsMatch(document.FinalUrl.AbsolutePath)
        && (document.ContentType?.Contains("html", StringComparison.OrdinalIgnoreCase) ?? true);

    /// <inheritdoc />
    public async Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanRead(document))
            return Failure("The document is not public Craftfair event HTML.");
        IDocument html = await new HtmlParser().ParseDocumentAsync(Encoding.UTF8.GetString(document.Body.Span), cancellationToken);
        if (document.FinalUrl.AbsolutePath.TrimEnd('/') == "/events")
        {
            var discovered = html.QuerySelectorAll(".event-card-title a[href], a[rel='next'][href]")
                .Select(element => Resolve(document.FinalUrl, element.GetAttribute("href")))
                .Where(uri => uri is not null && uri.IdnHost == document.FinalUrl.IdnHost && EventPath().IsMatch(uri.AbsolutePath))
                .Select(uri => new DiscoveredUrl { Url = uri!.AbsoluteUri, IsListing = uri.AbsolutePath.TrimEnd('/') == "/events" })
                .DistinctBy(item => item.Url).ToArray();
            return Result<CaptureReadResult>.Ok(new() { Discovered = discovered, Complete = false });
        }

        string title = Clean(html.QuerySelector("h1.h1, h1")?.TextContent);
        if (title.Length == 0 || html.QuerySelector(".event-detail-panel") is null)
            return Failure("The Craftfair event detail is missing its title or event panel.");
        var fields = html.QuerySelectorAll(".sidebar-meta-item")
            .Select(item => new { Label = Clean(item.QuerySelector(".meta-label")?.TextContent), Value = Clean(item.QuerySelector(".meta-val")?.TextContent) })
            .Where(item => item.Label.Length > 0)
            .GroupBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
        fields.TryGetValue("Event dates", out string? dateText);
        fields.TryGetValue("Registration deadline", out string? deadlineText);
        fields.TryGetValue("Location", out string? location);
        var dates = Dates(dateText);
        string statusText = Clean(html.QuerySelector(".status-badge")?.TextContent);
        bool closed = statusText.Contains("closed", StringComparison.OrdinalIgnoreCase);
        string? deadline = html.QuerySelector(".countdown-minimal[data-deadline]")?.GetAttribute("data-deadline");
        if (deadline is null || !DateTimeOffset.TryParseExact(deadline, "yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            deadline = Day(deadlineText);
        string text = Clean(html.QuerySelector(".event-detail-panel .detail-rich-text")?.TextContent);
        string canonical = new UriBuilder(document.FinalUrl) { Query = string.Empty, Fragment = string.Empty }.Uri.AbsoluteUri;
        string locale = html.DocumentElement?.GetAttribute("lang") ?? "en";
        var evidence = new
        {
            reader = "craftfair", recordKind = "event", fields, eventDatesRaw = dateText,
            applicationDeadline = deadline, applicationDeadlineRaw = deadlineText, applicationStatusRaw = statusText,
            participation = new { artisan = closed ? "closed" : "unknown", visitor = "unknown", vendor = "unknown", exhibitor = "unknown", runway = "unknown" },
            eligibility = "unknown", fees = "unknown",
        };
        var node = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "Event", ["@id"] = canonical,
            ["name"] = title, ["description"] = text, ["url"] = canonical, ["inLanguage"] = locale,
            ["craftfair"] = evidence,
        };
        if (dates.Start is not null) node["startDate"] = dates.Start;
        if (dates.End is not null) node["endDate"] = dates.End;
        if (!string.IsNullOrWhiteSpace(location)) node["location"] = new { name = location };
        var links = html.QuerySelectorAll(".event-detail-panel a[href], .event-side-panel a[href]")
            .Where(anchor => anchor.GetAttribute("href") is { } href && !href.TrimStart().StartsWith('#'))
            .Select(anchor => new { Url = Resolve(document.FinalUrl, anchor.GetAttribute("href")), Text = Clean(anchor.TextContent) })
            .Where(link => link.Url is not null)
            .Select(link => new CaptureLink { Url = link.Url!.AbsoluteUri, Text = link.Text })
            .DistinctBy(link => link.Url).ToArray();
        string? image = Resolve(document.FinalUrl, html.QuerySelector(".event-banner img[src]")?.GetAttribute("src"))?.AbsoluteUri;
        CaptureMedia[] media = image is null ? [] : [new() { Url = image, Kind = MediaKind.Image, Position = 0 }];
        JsonElement structured = JsonSerializer.SerializeToElement(node);
        var capture = new Capture
        {
            ExternalKey = canonical, Url = canonical, OriginKey = canonical, Locale = locale,
            RequestUrl = document.RequestUrl.AbsoluteUri, FinalUrl = document.FinalUrl.AbsoluteUri,
            CapturedAtUtc = document.FetchedAtUtc, HttpStatus = document.StatusCode, ContentType = document.ContentType, Body = document.Body,
            ContentProjection = JsonSerializer.Serialize(new { canonical, structured, links, media }),
            Structured = [structured], Links = links, Media = media,
            Warnings = dates.Start is null ? ["event_date_unknown"] : [],
        };
        return Result<CaptureReadResult>.Ok(new() { Captures = [capture], Complete = false });
    }

    private static (string? Start, string? End) Dates(string? raw)
    {
        Match match = SameMonthRange().Match(raw ?? string.Empty);
        if (match.Success)
        {
            string? start = Day(match.Groups[1].Value + " " + match.Groups[3].Value + ", " + match.Groups[4].Value);
            string? end = Day(match.Groups[2].Value + " " + match.Groups[3].Value + ", " + match.Groups[4].Value);
            return start is not null && end is not null && string.CompareOrdinal(start, end) <= 0 ? (start, end) : (null, null);
        }
        string? day = Day(raw);
        return (day, day);
    }

    private static string? Day(string? raw) =>
        DateOnly.TryParseExact(raw, ["d MMMM, yyyy", "dd MMMM, yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
            ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

    private static Uri? Resolve(Uri basis, string? raw) =>
        !string.IsNullOrWhiteSpace(raw) && Uri.TryCreate(basis, raw, out Uri? uri) && uri.Scheme is "https" or "http" && uri.UserInfo.Length == 0
            ? new UriBuilder(uri) { Fragment = string.Empty }.Uri : null;

    private static Result<CaptureReadResult> Failure(string message) => Result<CaptureReadResult>.Fail(AppErrorFactory.SerializationFailed(message));
    private static string Clean(string? text) => Space().Replace(text ?? string.Empty, " ").Trim().Normalize(NormalizationForm.FormC);

    [GeneratedRegex(@"^\s*(\d{1,2})\s*[-–—]\s*(\d{1,2})\s+([A-Za-z]+),?\s+(20\d{2})\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex SameMonthRange();
    [GeneratedRegex(@"^/events(?:/[^/]+)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex EventPath();
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Space();
}
