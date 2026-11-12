using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using Capture = WoW.Two.Sdk.Beta.Be.Harvest.Readers.Capture;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Caex;

/// <summary>Reads CAEx venue calendar rows without treating detail URL dates or access links as event facts.</summary>
public sealed partial class CaexCaptureReader : ICaptureReader, IFetchDestinationPolicy
{
    /// <inheritdoc />
    public string Key => CaexSourceAdapter.AdapterKey;

    /// <inheritdoc />
    public bool Allows(Uri destination) => SameOrigin(destination)
        && destination.AbsolutePath.TrimEnd('/') == CaexSourceAdapter.CalendarPath
        && destination.Query.Length == 0 && destination.Fragment.Length == 0;

    /// <inheritdoc />
    public bool CanRead(FetchedDocument document) => Allows(document.FinalUrl)
        && (document.ContentType?.Contains("html", StringComparison.OrdinalIgnoreCase) ?? true);

    /// <inheritdoc />
    public async Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default)
    {
        if (!CanRead(document))
            return Failure("The document is not the public CAEx venue calendar.");

        IDocument html = await new HtmlParser().ParseDocumentAsync(Encoding.UTF8.GetString(document.Body.Span), cancellationToken);
        var captures = new List<Capture>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (IElement row in html.QuerySelectorAll(".caex-event-row"))
        {
            IElement? heading = row.QuerySelector(".wp-block-post-title a[href]");
            string title = Text(heading);
            if (title.Length == 0 || ResolveLink(document.FinalUrl, heading?.GetAttribute("href")) is not { } detail
                || !SameOrigin(detail) || !DetailPath().IsMatch(detail.AbsolutePath))
                continue;

            string canonical = new UriBuilder(detail) { Query = string.Empty, Fragment = string.Empty }.Uri.AbsoluteUri;
            if (!seen.Add(canonical))
                continue;

            string rawDate = Text(row.QuerySelector(".caex-event-row-date"));
            var dates = ReadDates(rawDate);
            string organizer = Text(row.QuerySelector(".caex-event-row-organizer"));
            string description = Text(row.QuerySelector(".wp-block-post-excerpt__excerpt"));
            string calendar = document.FinalUrl.AbsoluteUri;
            var node = new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "Event",
                ["@id"] = canonical,
                ["url"] = canonical,
                ["name"] = title,
                ["description"] = description.Length > 0 ? description : title,
                ["inLanguage"] = "en",
                ["location"] = new { name = "CAEx Uzbekistan", address = new { addressLocality = "Tashkent", addressCountry = "UZ" } },
                ["caex"] = new { recordKind = "event", evidenceRole = "venue-calendar", calendarUrl = calendar, dateRaw = rawDate },
            };
            if (organizer.Length > 0)
                node["organizer"] = new { name = organizer };
            if (dates.Start is { } start)
                node["startDate"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (dates.End is { } end)
                node["endDate"] = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            List<CaptureLink> links = [new() { Url = calendar, Text = "Venue calendar" }];
            string? website = row.QuerySelector(".caex-event-row-website a[href]")?.GetAttribute("href");
            if (Uri.TryCreate(website, UriKind.Absolute, out Uri? organizerSite) && IsPublicWebLink(organizerSite))
                links.Add(new() { Url = organizerSite.AbsoluteUri, Text = "Event website" });

            JsonElement structured = JsonSerializer.SerializeToElement(node);
            captures.Add(new()
            {
                ExternalKey = canonical,
                OriginKey = canonical,
                Url = canonical,
                RequestUrl = document.RequestUrl.AbsoluteUri,
                FinalUrl = document.FinalUrl.AbsoluteUri,
                CapturedAtUtc = document.FetchedAtUtc,
                HttpStatus = document.StatusCode,
                ContentType = document.ContentType,
                Body = document.Body,
                Locale = "en",
                ContentProjection = JsonSerializer.Serialize(new { structured, links }),
                Structured = [structured],
                Links = links,
                Warnings = dates.Start is null ? ["calendar_date_unknown"] : [],
            });
        }

        return captures.Count == 0 ? Failure("No readable CAEx event rows were found.")
            : Result<CaptureReadResult>.Ok(new() { Captures = captures, Complete = false });
    }

    private static (DateOnly? Start, DateOnly? End) ReadDates(string label)
    {
        Match date = MonthFirst().Match(label);
        if (date.Success && TryDate(date, "start", out var start))
        {
            if (!date.Groups["end"].Success)
                return (start, null);
            return TryDate(date, "end", out var end) && end >= start ? (start, end) : (null, null);
        }

        date = DayFirst().Match(label);
        return date.Success && TryDate(date, "start", out start) ? (start, null) : (null, null);
    }

    private static bool TryDate(Match match, string day, out DateOnly date) =>
        DateOnly.TryParseExact($"{match.Groups["month"].Value} {match.Groups[day].Value} {match.Groups["year"].Value}",
            ["MMMM d yyyy", "MMM d yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
        && date.Year is >= 2000 and <= 2100;

    private static Uri? ResolveLink(Uri page, string? link) =>
        !string.IsNullOrWhiteSpace(link) && Uri.TryCreate(page, link, out Uri? url) && IsPublicWebLink(url) ? url : null;

    private static bool SameOrigin(Uri url) => url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps
        && url.IdnHost == CaexSourceAdapter.Host && url.IsDefaultPort && url.UserInfo.Length == 0;

    private static bool IsPublicWebLink(Uri url) =>
        url.IsAbsoluteUri && url.Scheme is "http" or "https" && url.UserInfo.Length == 0;

    private static string Text(IElement? element) => Spaces().Replace(element?.TextContent.Trim() ?? "", " ");

    private static Result<CaptureReadResult> Failure(string message) => Result<CaptureReadResult>.Fail(AppErrorFactory.Validation(message));

    [GeneratedRegex(@"^/[0-9]{4}/[0-9]{2}/[0-9]{2}/[^/]+/?$", RegexOptions.CultureInvariant)]
    private static partial Regex DetailPath();

    [GeneratedRegex(@"^(?<month>[a-zA-Z]+)\s+(?<start>[0-9]{1,2})(?:\s*[-–—]\s*(?<end>[0-9]{1,2}))?,\s*(?<year>[0-9]{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex MonthFirst();

    [GeneratedRegex(@"^(?<start>[0-9]{1,2})\s+(?<month>[a-zA-Z]+),\s*(?<year>[0-9]{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex DayFirst();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}
