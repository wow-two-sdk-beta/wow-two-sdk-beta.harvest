using System.Text;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Joymee;

internal sealed class ReplayInputException(string message) : Exception(message);

internal static class ListingInputs
{
    private const string ArchiveName = "OLX archive";
    private const string LocalName = "Local OLX";
    private const string JoymeeName = "Joymee";

    internal static IEnumerable<PostEnvelope> ArchivedOlx(string directory)
    {
        if (!Directory.Exists(directory))
            throw new ReplayInputException("The OLX archive directory could not be opened.");

        foreach (string batch in Directory.EnumerateFiles(directory, "batch*.json").Order(StringComparer.Ordinal))
        {
            int rowNumber = 0;
            foreach (JsonElement row in Rows(batch, ArchiveName))
            {
                rowNumber++;
                if (Property(row, "parameters").ValueKind != JsonValueKind.Array)
                    throw InvalidRow(ArchiveName, rowNumber);

                var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
                string? sellerCategory = null;
                foreach (JsonElement parameter in Property(row, "parameters").EnumerateArray())
                {
                    if (parameter.ValueKind != JsonValueKind.String || parameter.GetString() is not { } text)
                        continue;
                    int separator = text.IndexOf(':');
                    if (separator > 0)
                        attributes.TryAdd(text[..separator].Trim(), text[(separator + 1)..].Trim());
                    else if (text is "Бизнес" or "Частное лицо")
                    {
                        sellerCategory = text;
                        attributes.TryAdd(text, text);
                    }
                }

                string? subregion = Text(row, "subregion");
                yield return new PostEnvelope
                {
                    Kind = PostKind.Listing,
                    Title = Text(row, "title"),
                    Text = Text(row, "description"),
                    Extras = new Dictionary<string, JsonElement>
                    {
                        ["source"] = JsonSerializer.SerializeToElement(new
                        {
                            schema = "olx.ad.v1",
                            parserVersion = "archive-replay@1",
                            fields = new
                            {
                                priceText = Text(row, "price"),
                                postedAtText = Text(row, "posted_at"),
                                subregion,
                                location = DistrictLabel(subregion) ? new { districtName = subregion } : null,
                                isNegotiable = Boolean(row, "is_negotiable"),
                                sellerCategory,
                                attributes,
                            },
                        }),
                    },
                    Extraction = new() { NormVersion = "archive-replay@1", RulesVersion = "archive-replay@1" },
                };
            }
        }
    }

    internal static IEnumerable<(PostEnvelope Envelope, string Url)> LocalOlx(string file)
    {
        int rowNumber = 0;
        foreach (JsonElement row in Rows(file, LocalName))
        {
            rowNumber++;
            if (Text(row, "url") is not { Length: > 0 } url || Property(row, "envelope").ValueKind != JsonValueKind.Object)
                throw InvalidRow(LocalName, rowNumber);
            PostEnvelope? envelope;
            try
            {
                envelope = Property(row, "envelope").Deserialize<PostEnvelope>(JsonOptionsConstants.Default);
            }
            catch (JsonException)
            {
                throw InvalidRow(LocalName, rowNumber);
            }

            yield return (envelope ?? throw InvalidRow(LocalName, rowNumber), url);
        }
    }

    internal static async Task ReplayJoymeeAsync(string file, ReplayReport report)
    {
        var reader = new JoymeeCaptureReader();
        var extractor = new RulesPostExtractor();
        var request = new ExtractionRequest { Family = SourceFamily.Site, Ai = AiMode.Off };
        int rowNumber = 0;
        foreach (JsonElement row in Rows(file, JoymeeName))
        {
            rowNumber++;
            if (!Uri.TryCreate(Text(row, "url"), UriKind.Absolute, out Uri? url))
                throw InvalidRow(JoymeeName, rowNumber);
            JsonElement body = Property(row, "body");
            string raw = body.ValueKind switch
            {
                JsonValueKind.Object => body.GetRawText(),
                JsonValueKind.String when body.GetString() is { Length: > 0 } value => value,
                _ => throw InvalidRow(JoymeeName, rowNumber),
            };
            var capturedAt = Property(row, "capturedAtUtc");
            DateTimeOffset fetchedAt = capturedAt.ValueKind == JsonValueKind.Undefined
                ? DateTimeOffset.UnixEpoch
                : capturedAt.ValueKind == JsonValueKind.String && capturedAt.TryGetDateTimeOffset(out var parsed)
                    ? parsed.ToUniversalTime()
                    : throw InvalidRow(JoymeeName, rowNumber);

            var document = new FetchedDocument
            {
                RequestUrl = url,
                FinalUrl = url,
                StatusCode = 200,
                ContentType = "application/json",
                Body = Encoding.UTF8.GetBytes(raw),
                FetchedAtUtc = fetchedAt,
            };
            if (await reader.ReadAsync(document) is not Result<CaptureReadResult>.Success read)
            {
                report.RecordFailure("readerRejected");
                continue;
            }

            if (read.Value.Captures is not [var capture])
            {
                report.RecordFailure("captureMissing");
                continue;
            }

            if (await extractor.ExtractAsync(capture, request) is Result<PostEnvelope>.Success extracted)
                report.Record(extracted.Value, capture.Url);
            else
                report.RecordFailure("extractorRejected");
        }
    }

    internal static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var member) ? member : default;

    internal static string? Text(JsonElement value, string name) =>
        Property(value, name) is { ValueKind: JsonValueKind.String } member ? member.GetString() : null;

    private static bool? Boolean(JsonElement value, string name) => Property(value, name).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static bool DistrictLabel(string? text) =>
        text is not null && (text.EndsWith(" район", StringComparison.OrdinalIgnoreCase)
            || text.EndsWith(" tumani", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<JsonElement> Rows(string file, string cohort)
    {
        using JsonDocument document = ReadDocument(file, cohort);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new ReplayInputException($"{cohort} input must contain a JSON array.");
        foreach (JsonElement row in document.RootElement.EnumerateArray())
            yield return row;
    }

    private static JsonDocument ReadDocument(string file, string cohort)
    {
        try
        {
            using var stream = File.OpenRead(file);
            return JsonDocument.Parse(stream);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new ReplayInputException($"{cohort} input could not be read as JSON.");
        }
    }

    private static ReplayInputException InvalidRow(string cohort, int rowNumber) =>
        new($"{cohort} input row {rowNumber} has an invalid format.");
}
