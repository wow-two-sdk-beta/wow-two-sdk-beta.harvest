using System.Globalization;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;

internal sealed class ReplayReport
{
    private static readonly string[] Sections = ["property", "unit", "place", "offers", "publisher", "features"];
    private static readonly string[] NestedNativeFields = ["category", "detail", "location", "map", "media", "price", "pricing"];
    private static readonly HashSet<string> AttributeLabels = new(StringComparer.Ordinal)
    {
        "В квартире есть", "Высота потолков", "Год постройки", "Год постройки/сдачи", "Жилая площадь",
        "Количество комнат", "Комиссионные", "Меблирована", "Общая площадь", "Общая площадь, м²",
        "Планировка", "Площадь кухни", "Ремонт", "Рядом есть", "Санузел", "Тип жилья", "Тип объекта",
        "Тип сделки", "Тип строения", "Этаж", "Этажность дома", "Бизнес", "Частное лицо",
    };

    public int Observations { get; private set; }
    public int Extracted { get; private set; }
    public int Mapped { get; private set; }
    public int RowsWithFlags { get; private set; }
    public int RowsWithConflicts { get; private set; }
    public int RowsWithUnknownValues { get; private set; }
    public SortedDictionary<string, int> Failures { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> NativePresent { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> MappedPresent { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> EvidenceByFieldAndMethod { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> QualityFlags { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> UnknownValuesByFieldAndReason { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> UnmappedCodes { get; } = new(StringComparer.Ordinal);
    public bool Complete => Observations > 0 && Observations == Mapped;

    internal void RecordFailure(string reason)
    {
        Observations++;
        Increment(Failures, reason);
    }

    internal void Record(PostEnvelope envelope, string? url)
    {
        Observations++;
        Extracted++;
        if (envelope.Extras.TryGetValue("source", out var source))
        {
            var native = ListingInputs.Property(source, "fields");
            Count(NativePresent, NativeFields(native).Concat(NestedNativeFields.SelectMany(
                name => PresentPaths(ListingInputs.Property(native, name), "/" + name))));
        }

        if (ListingSchemaMapper.Map(envelope, url) is not { } output
            || ListingInputs.Text(output, "schema") != "wow2.listing.v1")
        {
            Increment(Failures, "projectionMissing");
            return;
        }

        Mapped++;
        var paths = Sections.SelectMany(section => PresentPaths(ListingInputs.Property(output, section), "/" + section));
        Count(MappedPresent, paths);
        Count(EvidenceByFieldAndMethod, ArrayItems(output, "evidence").Select(item => EvidenceKey(item, "method")));

        var flags = ArrayItems(output, "qualityFlags")
            .Select(flag => flag.ValueKind == JsonValueKind.String ? SchemaToken(flag.GetString()) : null)
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        Count(QualityFlags, flags);
        if (flags.Length > 0)
            RowsWithFlags++;
        if (flags.Any(flag => flag.Contains("conflict", StringComparison.OrdinalIgnoreCase)))
            RowsWithConflicts++;

        var unknown = ArrayItems(output, "unknownValues").ToArray();
        if (unknown.Length > 0)
            RowsWithUnknownValues++;
        Count(UnknownValuesByFieldAndReason, unknown.Select(item => EvidenceKey(item, "reason")));
        Count(UnmappedCodes, unknown.Select(UnmappedCodeKey));
    }

    private static IEnumerable<string> NativeFields(JsonElement fields)
    {
        if (fields.ValueKind != JsonValueKind.Object)
            yield break;
        foreach (var property in fields.EnumerateObject().Where(property => IsPresent(property.Value)))
            yield return "/" + (SchemaToken(property.Name) ?? "(unknown)");

        if (ListingInputs.Property(fields, "attributes") is { ValueKind: JsonValueKind.Object } attributes)
            foreach (var attribute in attributes.EnumerateObject().Where(attribute => IsPresent(attribute.Value)))
                yield return "/attributes/" + (AttributeLabels.Contains(attribute.Name) ? attribute.Name : "(other)");
    }

    private static IEnumerable<string> PresentPaths(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Object)
            return value.EnumerateObject().SelectMany(child => PresentPaths(child.Value,
                path + "/" + (SchemaToken(child.Name) ?? "(unknown)")));
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().SelectMany(child => PresentPaths(child, path + "/[]"));
        return IsPresent(value) ? [path] : [];
    }

    private static bool IsPresent(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => false,
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() > 0,
        JsonValueKind.Object => value.EnumerateObject().Any(),
        _ => true,
    };

    private static IEnumerable<JsonElement> ArrayItems(JsonElement output, string key) =>
        ListingInputs.Property(output, key) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];

    private static string? EvidenceKey(JsonElement item, string category)
    {
        string? field = NormalizeFieldPath(ListingInputs.Text(item, "path"));
        string? token = SchemaToken(ListingInputs.Text(item, category));
        return field is not null && token is not null ? field + " | " + token : null;
    }

    private static string? UnmappedCodeKey(JsonElement item)
    {
        string? field = NormalizeFieldPath(ListingInputs.Text(item, "path"));
        string? reason = ListingInputs.Text(item, "reason");
        string? raw = ListingInputs.Text(item, "raw");
        string? source = ListingInputs.Text(item, "sourcePath");
        string? sourceField = SchemaToken(source?.Split('/').LastOrDefault());
        return field is not null && reason is not null && reason.Contains("code", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int code) && code is >= 0 and <= 999
            ? field + " | " + (sourceField ?? "(source)") + " | " + code.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? NormalizeFieldPath(string? path)
    {
        if (path is not { Length: > 1 and <= 128 } || path[0] != '/')
            return null;
        string[] parts = path[1..].Split('/');
        if (!Sections.Contains(parts[0], StringComparer.Ordinal))
            return null;
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length > 0 && parts[i].All(char.IsAsciiDigit))
                parts[i] = "[]";
            else if (SchemaToken(parts[i]) is null)
                return null;
        }

        return "/" + string.Join('/', parts);
    }

    private static string? SchemaToken(string? value) =>
        value is { Length: > 0 and <= 80 } && char.IsAsciiLetter(value[0])
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
            ? value
            : null;

    private static void Count(SortedDictionary<string, int> counts, IEnumerable<string?> values)
    {
        foreach (string value in values.OfType<string>().Distinct(StringComparer.Ordinal))
            Increment(counts, value);
    }

    private static void Increment(SortedDictionary<string, int> counts, string key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;
}
