using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Anons;

internal static partial class AnonsEventProjector
{
    internal static JsonElement Project(JsonElement? original, string canonical, string title, string text, string locale,
        IReadOnlyDictionary<string, string> fields, IReadOnlyList<string> tags)
    {
        string? rawDates = Field(fields, "Дата", "Sana", "Date");
        string? rawTimes = Field(fields, "Время", "Vaqt", "Time");
        string? rawPrice = Field(fields, "Стоимость", "Narx", "Price");
        string? rawVenue = Field(fields, "Локация", "Адрес", "Manzil", "Joy", "Location", "Address");
        bool free = rawPrice is not null && FreeAdmission().IsMatch(rawPrice);
        var evidence = new
        {
            reader = "anons", recordKind = ApplicationTitle().IsMatch(title) ? "call" : "event",
            rawDates, rawTimes, rawPrice, rawVenue, fields, tags,
            visitorAdmission = free ? "free" : "unknown",
            participation = new { visitor = "unknown", vendor = "unknown", exhibitor = "unknown", runway = "unknown", artisan = "unknown" },
        };
        var node = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "Event", ["@id"] = canonical,
            ["url"] = canonical, ["name"] = title, ["description"] = text, ["inLanguage"] = locale, ["anons"] = evidence,
        };
        foreach (string property in new[] { "location", "organizer", "eventStatus", "eventAttendanceMode", "image", "offers", "isAccessibleForFree" })
            if (original is { } json && json.TryGetProperty(property, out JsonElement value)
                && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                node[property] = value.Clone();
        foreach (string property in new[] { "startDate", "endDate" })
            if (ValidDate(AnonsStructuredData.String(original, property)) is { } value)
                node[property] = value;
        if (!node.ContainsKey("location") && rawVenue is not null)
            node["location"] = new Dictionary<string, object?>
            {
                ["@type"] = "Place", ["address"] = new Dictionary<string, object?>
                {
                    ["@type"] = "PostalAddress", ["streetAddress"] = rawVenue,
                },
            };
        if (!node.ContainsKey("offers") && Price(rawPrice, free) is { } offer)
            node["offers"] = offer;
        if (free)
            node["isAccessibleForFree"] = true;
        return JsonSerializer.SerializeToElement(node);
    }

    private static string? Field(IReadOnlyDictionary<string, string> fields, params string[] labels) => fields
        .FirstOrDefault(pair => labels.Any(label => pair.Key.StartsWith(label, StringComparison.OrdinalIgnoreCase))).Value;

    private static object? Price(string? raw, bool free)
    {
        if (raw is null)
            return null;
        var offer = new Dictionary<string, object?> { ["@type"] = "Offer" };
        if (free)
        {
            offer["price"] = "0";
            return offer;
        }
        string text = Currency().Replace(raw, string.Empty).Trim();
        Match range = PriceRange().Match(text);
        Match single = SinglePrice().Match(text);
        if (range.Success && Amount(range.Groups[1].Value) is { } minimumPrice && Amount(range.Groups[2].Value) is { } maximumPrice)
        {
            offer["@type"] = "AggregateOffer";
            offer["lowPrice"] = minimumPrice;
            offer["highPrice"] = maximumPrice;
        }
        else if (single.Success && Amount(single.Groups[2].Value) is { } singlePrice)
        {
            bool minimum = single.Groups[1].Success && single.Groups[1].Value.Length > 0;
            offer["@type"] = minimum ? "AggregateOffer" : "Offer";
            offer[minimum ? "lowPrice" : "price"] = singlePrice;
        }
        else
            return null;
        if (Currency().IsMatch(raw))
            offer["priceCurrency"] = "UZS";
        return offer;
    }

    private static string? Amount(string raw) => decimal.TryParse(raw.Replace(" ", string.Empty, StringComparison.Ordinal)
        .Replace("\u00a0", string.Empty, StringComparison.Ordinal).Replace(',', '.'), NumberStyles.Number,
        CultureInfo.InvariantCulture, out decimal amount) ? amount.ToString(CultureInfo.InvariantCulture) : null;

    private static string? ValidDate(string? raw)
    {
        if (raw is null)
            return null;
        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return raw;
        return OffsetDate().IsMatch(raw) && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? raw : null;
    }

    [GeneratedRegex(@"вход\s+(?:свободн\w*|бесплатн\w*)|бесплатн\w*\s+вход|kirish\s+bepul|free\s+(?:entry|admission)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FreeAdmission();

    [GeneratedRegex(@"при[её]м\s+заявок|заявки\s+на\s+(?:конкурс|участие)|открыт\w*\s+конкурс|ariza(?:lar)?\s+qabul|ochiq\s+tanlov", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationTitle();

    [GeneratedRegex(@"\bUZS\b|сум(?:ов|а)?|so['‘’ʻ]?m|сўм", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Currency();

    [GeneratedRegex(@"^\s*([0-9]+(?:[ \u00a0][0-9]{3})*(?:[.,][0-9]{1,2})?)\s*[-–—]\s*([0-9]+(?:[ \u00a0][0-9]{3})*(?:[.,][0-9]{1,2})?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex PriceRange();

    [GeneratedRegex(@"^\s*(от\s+|from\s+)?([0-9]+(?:[ \u00a0][0-9]{3})*(?:[.,][0-9]{1,2})?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SinglePrice();

    [GeneratedRegex(@"^20\d{2}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex OffsetDate();
}
