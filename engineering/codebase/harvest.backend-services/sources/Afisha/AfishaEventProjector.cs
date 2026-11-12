using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Afisha;

/// <summary>Maps explicit Afisha fields into an event while keeping participation and missing dates honest.</summary>
internal static partial class AfishaEventProjector
{
    internal static (JsonElement? Event, JsonElement Evidence) Project(
        JsonElement? original,
        JsonElement? material,
        string title,
        string text,
        string locale,
        string canonical,
        IReadOnlyList<string> paragraphs)
    {
        string? materialType = AfishaEmbeddedData.String(material, "type");
        bool call = ApplicationTitle().IsMatch(title);
        bool announced = original is not null || materialType == "EVENT";
        string kind = announced ? "event" : call ? "call" : "article";
        JsonElement[] schedules = Schedules(material);
        JsonElement evidence = JsonSerializer.SerializeToElement(new
        {
            reader = "afisha",
            recordKind = kind,
            materialId = AfishaEmbeddedData.String(material, "@id"),
            materialType,
            sections = Sections(material),
            schedules = schedules.Select(ScheduleEvidence).ToArray(),
            applicationDeadlineEvidence = Deadline(paragraphs),
            participation = new { visitor = "unknown", vendor = "unknown", exhibitor = "unknown", runway = "unknown", artisan = "unknown" },
        });
        if (!announced && !call)
            return (null, evidence);

        var node = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "Event", ["@id"] = canonical,
            ["name"] = title, ["description"] = text, ["url"] = canonical, ["inLanguage"] = locale,
            ["afisha"] = evidence,
        };
        foreach (string property in new[] { "location", "organizer", "eventStatus", "eventAttendanceMode", "image", "offers" })
            if (original is { } json && json.TryGetProperty(property, out JsonElement value)
                && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                node[property] = value.Clone();

        string? start = ValidDate(AfishaEmbeddedData.String(original, "startDate"));
        string? end = ValidDate(AfishaEmbeddedData.String(original, "endDate"));
        JsonElement? schedule = schedules.FirstOrDefault(value => start is not null
            && AfishaEmbeddedData.String(value, "startedAt")?.StartsWith(start[..10], StringComparison.Ordinal) == true);
        if (schedule is { ValueKind: JsonValueKind.Undefined })
            schedule = schedules.FirstOrDefault();
        if (schedule is { ValueKind: JsonValueKind.Object } selected)
        {
            start ??= ScheduleDate(selected, "startedAt", "isExactStartTime");
            end ??= ScheduleDate(selected, "endedAt", "isExactEndTime");
            if (!node.ContainsKey("location") && Location(selected) is { } location)
                node["location"] = location;
            else if (node.TryGetValue("location", out object? stated) && stated is JsonElement pageLocation
                && WithLocality(pageLocation, selected) is { } completed)
                node["location"] = completed;
            if (!node.ContainsKey("offers") && Offer(selected, text) is { } offer)
                node["offers"] = offer;
        }
        if (start is not null)
            node["startDate"] = start;
        if (end is not null)
            node["endDate"] = end;
        return (JsonSerializer.SerializeToElement(node), evidence);
    }

    private static JsonElement[] Schedules(JsonElement? material)
    {
        return material is { ValueKind: JsonValueKind.Object } node && node.TryGetProperty("eventSchedules", out JsonElement schedules)
            && schedules.ValueKind == JsonValueKind.Array ? schedules.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToArray() : [];
    }

    private static string[] Sections(JsonElement? material)
    {
        return material is { ValueKind: JsonValueKind.Object } node && node.TryGetProperty("sections", out JsonElement sections)
            && sections.ValueKind == JsonValueKind.Array
            ? sections.EnumerateArray().Select(item => AfishaEmbeddedData.String(item, "slug"))
                .Where(value => value is not null).Cast<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
            : [];
    }

    private static object ScheduleEvidence(JsonElement schedule)
    {
        return new
        {
            id = AfishaEmbeddedData.String(schedule, "@id"),
            starts = ScheduleDate(schedule, "startedAt", "isExactStartTime"),
            ends = ScheduleDate(schedule, "endedAt", "isExactEndTime"),
            dates = schedule.TryGetProperty("dates", out JsonElement dates) && dates.ValueKind == JsonValueKind.Array
                ? dates.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => ValidDate(value.GetString())).Where(value => value is not null).ToArray() : [],
            place = Location(schedule),
        };
    }

    private static string? ScheduleDate(JsonElement schedule, string property, string exactProperty)
    {
        string? value = ValidDate(AfishaEmbeddedData.String(schedule, property));
        if (value is null)
            return null;
        bool exact = schedule.TryGetProperty(exactProperty, out JsonElement flag) && flag.ValueKind == JsonValueKind.True;
        return exact ? value : value[..10];
    }

    private static object? Location(JsonElement schedule)
    {
        if (!schedule.TryGetProperty("place", out JsonElement place) || place.ValueKind != JsonValueKind.Object)
            return null;
        string? name = AfishaEmbeddedData.String(place, "name");
        string? street = AfishaEmbeddedData.String(place, "address");
        string? city = City(place);
        return name is null && street is null && city is null ? null : new Dictionary<string, object?>
        {
            ["@type"] = "Place", ["name"] = name,
            ["address"] = new Dictionary<string, object?>
            {
                ["@type"] = "PostalAddress", ["addressLocality"] = city, ["streetAddress"] = street,
            },
        };
    }

    /// <summary>Completes the page's own JSON-LD place with the schedule's city when its address names no locality.</summary>
    /// <remarks>
    ///   - Afisha's JSON-LD writes the address as one string, so the shared rules see a venue and an address but no city
    ///   - the schedule's place supplies the city, and its street when the page's address is that string
    ///   - a schedule naming another venue leaves the page's place untouched
    /// </remarks>
    private static Dictionary<string, object?>? WithLocality(JsonElement location, JsonElement schedule)
    {
        if (location.ValueKind != JsonValueKind.Object || !schedule.TryGetProperty("place", out JsonElement place)
            || place.ValueKind != JsonValueKind.Object || City(place) is not { Length: > 0 } city)
            return null;
        string? pageVenue = AfishaEmbeddedData.String(location, "name");
        string? scheduleVenue = AfishaEmbeddedData.String(place, "name");
        if (pageVenue is not null && scheduleVenue is not null
            && !string.Equals(pageVenue.Trim(), scheduleVenue.Trim(), StringComparison.OrdinalIgnoreCase))
            return null;
        location.TryGetProperty("address", out JsonElement address);
        if (address.ValueKind == JsonValueKind.Object && AfishaEmbeddedData.String(address, "addressLocality") is { Length: > 0 })
            return null;

        Dictionary<string, object?> completed = Copy(location);
        Dictionary<string, object?> postal = address.ValueKind == JsonValueKind.Object
            ? Copy(address) : new Dictionary<string, object?> { ["@type"] = "PostalAddress" };
        postal["addressLocality"] = city;
        if (!postal.ContainsKey("streetAddress")
            && (AfishaEmbeddedData.String(place, "address") ?? (address.ValueKind == JsonValueKind.String ? address.GetString() : null))
                is { Length: > 0 } street)
            postal["streetAddress"] = street;
        completed["address"] = postal;
        return completed;
    }

    private static string? City(JsonElement place)
    {
        return place.TryGetProperty("city", out JsonElement city) ? AfishaEmbeddedData.String(city, "name") : null;
    }

    private static Dictionary<string, object?> Copy(JsonElement node)
    {
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (JsonProperty property in node.EnumerateObject())
            copy[property.Name] = property.Value.Clone();
        return copy;
    }

    private static object? Offer(JsonElement schedule, string text)
    {
        if (!schedule.TryGetProperty("remoteProperties", out JsonElement providers) || providers.ValueKind != JsonValueKind.Object)
            return null;
        foreach (JsonProperty provider in providers.EnumerateObject())
        {
            string? price = Number(provider.Value, "attendancePrice");
            string? low = Number(provider.Value, "attendanceMinPrice");
            string? high = Number(provider.Value, "attendanceMaxPrice");
            if (price is null && low is null)
                continue;
            var offer = new Dictionary<string, object?> { ["@type"] = price is null ? "AggregateOffer" : "Offer" };
            if (price is not null)
                offer["price"] = price;
            else
            {
                offer["lowPrice"] = low;
                if (high is not null)
                    offer["highPrice"] = high;
            }
            if (UzbekCurrency().IsMatch(text))
                offer["priceCurrency"] = "UZS";
            return offer;
        }
        return null;
    }

    private static string? Number(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out JsonElement value))
            return null;
        string? raw = value.ValueKind == JsonValueKind.String ? value.GetString()
            : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount) && amount >= 0
            ? amount.ToString(CultureInfo.InvariantCulture) : null;
    }

    private static object? Deadline(IReadOnlyList<string> paragraphs)
    {
        string? evidence = paragraphs.FirstOrDefault(value => DeadlineWords().IsMatch(value));
        if (evidence is null)
            return null;
        string? date = null;
        Match deadline = DeadlineDate().Match(evidence);
        if (deadline.Success)
        {
            string raw = deadline.Groups["date"].Value;
            date = ValidDate(raw);
            if (date is null && DateOnly.TryParseExact(raw, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly value))
                date = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        return new { raw = evidence.Length > 500 ? evidence[..500] : evidence, date };
    }

    private static string? ValidDate(string? raw)
    {
        if (raw is null)
            return null;
        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return raw;
        return OffsetDate().IsMatch(raw)
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? raw : null;
    }

    [GeneratedRegex(@"при[её]м\s+заявок|заявки\s+на\s+(?:конкурс|участие)|открыт\w*\s+конкурс|ariza(?:lar)?\s+qabul|ochiq\s+tanlov", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationTitle();

    [GeneratedRegex(@"заяв(?:к|ок)|дедлайн|крайний\s+срок|ariza|muddat|ариза", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeadlineWords();

    [GeneratedRegex(@"(?:\bдо|не\s+позднее|дедлайн|deadline|until|muddat)\s*[:—-]?\s*(?<date>20\d{2}-\d{2}-\d{2}|\d{2}\.\d{2}\.20\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeadlineDate();

    [GeneratedRegex(@"^20\d{2}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex OffsetDate();

    [GeneratedRegex(@"\bUZS\b|сум|so['‘’ʻ]?m|сўм", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UzbekCurrency();
}
