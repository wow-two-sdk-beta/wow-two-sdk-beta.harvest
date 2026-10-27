using System.Globalization;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;

/// <summary>Reads iCalendar VEVENT records with folded lines, all-day precision and explicit time zones.</summary>
public sealed class IcsCaptureReader : ICaptureReader
{
    /// <inheritdoc />
    public string Key => "ics";

    /// <inheritdoc />
    public bool CanRead(FetchedDocument document) => ReaderSupport.Decode(document).TrimStart().StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanRead(document)) return Task.FromResult(ReaderSupport.Invalid("iCalendar"));
        var lines = ReaderSupport.Decode(document).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n ", string.Empty, StringComparison.Ordinal).Replace("\n\t", string.Empty, StringComparison.Ordinal).Split('\n');
        var captures = new List<Capture>();
        Dictionary<string, (string Value, string? Zone)>? fields = null;
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase)) { fields = new(StringComparer.OrdinalIgnoreCase); continue; }
            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase) && fields is not null)
            {
                var capture = ReadEvent(document, fields);
                if (capture is not null) captures.Add(capture);
                fields = null;
                continue;
            }
            var colon = line.IndexOf(':');
            if (fields is null || colon < 0) continue;
            var declaration = line[..colon].Split(';');
            var zone = declaration.Skip(1).FirstOrDefault(value => value.StartsWith("TZID=", StringComparison.OrdinalIgnoreCase))?[5..].Trim('"');
            fields.TryAdd(declaration[0], (Unescape(line[(colon + 1)..]), zone));
        }
        if (fields is not null || !lines.Any(line => line.Equals("END:VCALENDAR", StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(ReaderSupport.Invalid("iCalendar"));
        return Task.FromResult(Result<CaptureReadResult>.Ok(new() { Captures = captures, Complete = false }));
    }

    private static Capture? ReadEvent(FetchedDocument document, Dictionary<string, (string Value, string? Zone)> fields)
    {
        string? Value(string key) => fields.GetValueOrDefault(key).Value;
        if (Value("UID") is not { Length: > 0 } uid) return null;
        var start = Date(Value("DTSTART"));
        var end = Date(Value("DTEND"));
        var url = ReaderSupport.Url(document.FinalUrl, Value("URL"))
            ?? document.FinalUrl.AbsoluteUri + "#event-" + Uri.EscapeDataString(uid);
        var title = Value("SUMMARY");
        var text = Value("DESCRIPTION");
        var recurring = Value("RRULE") is not null;
        // Recurrences stay explicit: this reader captures the master instead of inventing instances.
        var node = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["@type"] = "Event", ["@id"] = uid, ["name"] = title, ["description"] = text,
            ["url"] = url, ["startDate"] = start, ["endDate"] = end,
            ["location"] = Value("LOCATION"), ["eventStatus"] = Value("STATUS")?.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) == true
                ? "https://schema.org/EventCancelled" : null,
            ["datePublished"] = Date(Value("CREATED")), ["timeZone"] = fields.GetValueOrDefault("DTSTART").Zone,
            ["recurrenceRule"] = Value("RRULE"),
        });
        var externalKey = Value("RECURRENCE-ID") is { } recurrence ? $"{uid}/{recurrence}" : uid;
        return ReaderSupport.Capture(document, externalKey, url, node.GetRawText(), [node], externalKey,
            published: ReaderSupport.Timestamp(Date(Value("CREATED"))), warnings: recurring ? ["recurrence_master_only"] : []);
    }

    private static string? Date(string? value)
    {
        if (value is null) return null;
        if (DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var utc = value.EndsWith('Z');
        var format = utc ? "yyyyMMdd'T'HHmmss'Z'" : "yyyyMMdd'T'HHmmss";
        return DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + (utc ? "Z" : string.Empty) : null;
    }

    private static string Unescape(string value) => value.Replace("\\n", "\n", StringComparison.OrdinalIgnoreCase)
        .Replace("\\,", ",", StringComparison.Ordinal).Replace("\\;", ";", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
}
