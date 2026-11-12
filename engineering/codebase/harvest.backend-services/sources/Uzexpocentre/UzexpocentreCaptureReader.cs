using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using Capture = WoW.Two.Sdk.Beta.Be.Harvest.Readers.Capture;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Uzexpocentre;

/// <summary>Reads exhibition rows with carried table cells and dates tied to the printed calendar year.</summary>
public sealed partial class UzexpocentreCaptureReader : ICaptureReader
{
    /// <inheritdoc />
    public string Key => "uzexpocentre-uz";
    /// <inheritdoc />
    public bool CanRead(FetchedDocument document) =>
        document.FinalUrl.IdnHost is "uzexpocentre.uz" or "www.uzexpocentre.uz"
        && document.FinalUrl.AbsolutePath.TrimEnd('/') == "/en/menu/krgazmalar-rezhasi"
        && (document.ContentType?.Contains("html", StringComparison.OrdinalIgnoreCase) ?? true);

    /// <inheritdoc />
    public async Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanRead(document))
            return Failure("The document is not the public Uzexpocentre exhibition calendar.");
        IDocument html = await new HtmlParser().ParseDocumentAsync(Encoding.UTF8.GetString(document.Body.Span), cancellationToken);
        IElement? content = html.QuerySelector(".content");
        if (content is null)
            return Failure("The exhibition calendar content is missing.");
        string canonical = new UriBuilder(document.FinalUrl) { Query = string.Empty, Fragment = string.Empty }.Uri.AbsoluteUri;
        var captures = new List<Capture>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (IElement table in content.QuerySelectorAll("table"))
        {
            int? year = PrintedYear(table);
            foreach (IElement?[] row in Rows(table))
            {
                if (row.Length < 5 || !RowNumber().IsMatch(Clean(row[0]?.TextContent)))
                    continue;
                string title = Clean(row[1]?.TextContent);
                if (title.Length == 0)
                    continue;
                string organizer = Clean(row[2]?.TextContent);
                string rawDate = Clean(row[3]?.TextContent);
                string pavilion = Clean(row[4]?.TextContent);
                var dates = Dates(rawDate, year);
                // A calendar URL represents many events. Row-local origins keep them distinct in the public post store.
                string edition = (year?.ToString(CultureInfo.InvariantCulture) ?? "undated") + "|" + title.ToLowerInvariant();
                occurrences.TryGetValue(edition, out int occurrence);
                occurrences[edition] = ++occurrence;
                string rowKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(edition)))[..24].ToLowerInvariant();
                string externalKey = canonical + "#harvest-row-" + rowKey
                    + (occurrence > 1 ? "-" + occurrence.ToString(CultureInfo.InvariantCulture) : string.Empty);
                var evidence = new
                {
                    reader = "uzexpocentre", recordKind = "event", calendarYear = year, dateRaw = rawDate, pavilion,
                    organizerRaw = organizer, sourceUrl = canonical,
                    participation = new { visitor = "unknown", vendor = "unknown", exhibitor = "unknown", runway = "unknown", artisan = "unknown" },
                    applicationDeadline = (string?)null, fees = "unknown",
                };
                var node = new Dictionary<string, object?>
                {
                    ["@context"] = "https://schema.org", ["@type"] = "Event", ["@id"] = externalKey,
                    ["name"] = title, ["description"] = title, ["url"] = canonical, ["inLanguage"] = "en",
                    ["organizer"] = new { name = organizer },
                    ["location"] = new
                    {
                        name = "Uzexpocentre" + (pavilion.Length > 0 ? " · " + pavilion : string.Empty),
                        address = new { addressLocality = "Tashkent", addressCountry = "UZ" },
                    },
                    ["uzexpocentre"] = evidence,
                };
                if (dates.Start is not null) node["startDate"] = dates.Start;
                if (dates.End is not null) node["endDate"] = dates.End;
                JsonElement structured = JsonSerializer.SerializeToElement(node);
                var links = row[2]?.QuerySelectorAll("a[href]").Select(anchor =>
                    Uri.TryCreate(document.FinalUrl, anchor.GetAttribute("href"), out Uri? url) && url.Scheme is "https" or "http" && url.UserInfo.Length == 0
                        ? new CaptureLink { Url = url.AbsoluteUri, Text = Clean(anchor.TextContent) } : null)
                    .Where(link => link is not null).Cast<CaptureLink>().DistinctBy(link => link.Url).ToArray() ?? [];
                captures.Add(new Capture
                {
                    ExternalKey = externalKey, OriginKey = externalKey, Url = canonical,
                    RequestUrl = document.RequestUrl.AbsoluteUri, FinalUrl = document.FinalUrl.AbsoluteUri,
                    CapturedAtUtc = document.FetchedAtUtc, HttpStatus = document.StatusCode, ContentType = document.ContentType,
                    Body = document.Body, Locale = "en",
                    ContentProjection = JsonSerializer.Serialize(new { externalKey, structured, links }),
                    Structured = [structured], Links = links,
                    Warnings = dates.Start is null ? ["calendar_date_unknown"] : [],
                });
            }
        }
        return captures.Count == 0 ? Failure("No readable exhibition rows were found.")
            : Result<CaptureReadResult>.Ok(new() { Captures = captures, Complete = false });
    }

    private static int? PrintedYear(IElement table)
    {
        // Only headings before this table are authoritative; a footer copyright year is not a calendar year.
        for (IElement? element = table.PreviousElementSibling; element is not null; element = element.PreviousElementSibling)
        {
            if (element.LocalName == "table") break;
            Match year = Year().Match(Clean(element.TextContent));
            if (year.Success) return int.Parse(year.Value, CultureInfo.InvariantCulture);
        }
        return null;
    }

    private static IEnumerable<IElement?[]> Rows(IElement table)
    {
        var carried = new Dictionary<int, (IElement Cell, int Left)>();
        foreach (IElement row in table.QuerySelectorAll("tr"))
        {
            var cells = new IElement?[5];
            foreach (var (column, value) in carried.ToArray())
            {
                if (column < cells.Length) cells[column] = value.Cell;
                if (value.Left <= 1) carried.Remove(column);
                else carried[column] = (value.Cell, value.Left - 1);
            }
            int index = 0;
            foreach (IElement cell in row.Children.Where(child => child.LocalName is "td" or "th"))
            {
                while (index < cells.Length && cells[index] is not null) index++;
                if (index >= cells.Length) break;
                int span = int.TryParse(cell.GetAttribute("colspan"), out int columns) ? Math.Clamp(columns, 1, 5) : 1;
                int rowSpan = int.TryParse(cell.GetAttribute("rowspan"), out int rows) ? Math.Clamp(rows, 1, 500) : 1;
                for (int offset = 0; offset < span && index + offset < cells.Length; offset++)
                {
                    cells[index + offset] = cell;
                    if (rowSpan > 1) carried[index + offset] = (cell, rowSpan - 1);
                }
                index += span;
            }
            yield return cells;
        }
    }

    private static (string? Start, string? End) Dates(string raw, int? year)
    {
        if (year is null) return (null, null);
        Match match = DottedRange().Match(raw.Replace(" ", string.Empty, StringComparison.Ordinal));
        if (!match.Success) return (null, null);
        int firstDay = int.Parse(match.Groups["first"].Value, CultureInfo.InvariantCulture);
        int lastDay = match.Groups["last"].Success ? int.Parse(match.Groups["last"].Value, CultureInfo.InvariantCulture) : firstDay;
        int month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
        int firstMonth = match.Groups["firstMonth"].Success ? int.Parse(match.Groups["firstMonth"].Value, CultureInfo.InvariantCulture) : month;
        try
        {
            var start = new DateOnly(year.Value, firstMonth, firstDay);
            var end = new DateOnly(year.Value, month, lastDay);
            return end < start ? (null, null) : (start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        catch (ArgumentOutOfRangeException) { return (null, null); }
    }

    private static Result<CaptureReadResult> Failure(string message) => Result<CaptureReadResult>.Fail(AppErrorFactory.SerializationFailed(message));
    private static string Clean(string? text) => Space().Replace(text ?? string.Empty, " ").Trim().Normalize(NormalizationForm.FormC);
    [GeneratedRegex(@"\b20\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Year();
    [GeneratedRegex(@"^\d{1,3}\.?$", RegexOptions.CultureInvariant)]
    private static partial Regex RowNumber();
    [GeneratedRegex(@"^(?<first>\d{1,2})(?:\.(?<firstMonth>\d{1,2}))?(?:[-–—](?<last>\d{1,2}))?\.(?<month>\d{1,2})\.?$", RegexOptions.CultureInvariant)]
    private static partial Regex DottedRange();
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Space();
}
