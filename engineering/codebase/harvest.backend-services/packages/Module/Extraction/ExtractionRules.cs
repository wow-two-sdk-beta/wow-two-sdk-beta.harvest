using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;

/// <summary>Extracts only values stated in source text; missing precision, currency and language remain unknown.</summary>
internal static partial class ExtractionRules
{
    internal static PostDate? Date(string? raw, DateRole role)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim();
        var iso = IsoDate().Match(value);
        if (!iso.Success || iso.Length != value.Length) return null;
        var year = int.Parse(iso.Groups["year"].Value, CultureInfo.InvariantCulture);
        if (year is < 1 or > 9999) return null;
        var precision = DatePrecision.Year;
        if (iso.Groups["month"].Success)
        {
            var month = int.Parse(iso.Groups["month"].Value, CultureInfo.InvariantCulture);
            if (month is < 1 or > 12) return null;
            precision = DatePrecision.Month;
            if (iso.Groups["day"].Success)
            {
                var day = int.Parse(iso.Groups["day"].Value, CultureInfo.InvariantCulture);
                if (day < 1 || day > DateTime.DaysInMonth(year, month)) return null;
                precision = DatePrecision.Day;
            }
        }
        if (iso.Groups["hour"].Success)
        {
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)) return null;
            precision = iso.Groups["second"].Success ? DatePrecision.Second
                : iso.Groups["minute"].Success ? DatePrecision.Minute : DatePrecision.Hour;
        }
        return new PostDate { Role = role, Value = value, Precision = precision, Raw = raw };
    }

    internal static IReadOnlyList<PostDate> Dates(string text)
    {
        var dates = new List<PostDate>();
        foreach (Match match in TextDate().Matches(text))
        {
            if (Date(match.Value, DateRole.Mentioned) is { } date) dates.Add(date);
        }
        foreach (Match match in EuropeanDate().Matches(text))
        {
            var iso = $"{match.Groups["year"].Value}-{match.Groups["month"].Value}-{match.Groups["day"].Value}";
            if (Date(iso, DateRole.Mentioned) is { } date) dates.Add(date with { Raw = match.Value });
        }
        return dates.DistinctBy(date => date.Value).ToList();
    }

    internal static IReadOnlyList<PostPrice> Prices(string text)
    {
        var result = new List<PostPrice>();
        foreach (Match match in Price().Matches(text))
        {
            var rawCurrency = match.Groups["currency"].Value;
            var currency = rawCurrency is "$" || rawCurrency.Equals("USD", StringComparison.OrdinalIgnoreCase) ? "USD"
                : rawCurrency is "€" || rawCurrency.Equals("EUR", StringComparison.OrdinalIgnoreCase) ? "EUR" : "UZS";
            if (!Amount(match.Groups["amount"].Value, out var amount)) continue;
            result.Add(new PostPrice { Amount = amount, Currency = currency, Raw = match.Value });
        }
        if (Free().IsMatch(text)) result.Add(new PostPrice { Free = true, Raw = Free().Match(text).Value });
        return result;
    }

    internal static bool Amount(string? raw, out decimal amount)
    {
        amount = 0;
        if (raw is null) return false;
        var value = raw.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("\u00a0", string.Empty, StringComparison.Ordinal);
        if (value.Contains(',') && !value.Contains('.'))
        {
            var fraction = value.Length - value.LastIndexOf(',') - 1;
            value = fraction is 1 or 2 ? value.Replace(',', '.') : value.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (value.Contains(',') && value.Contains('.')) value = value.Replace(",", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) && amount >= 0;
    }

    internal static IReadOnlyList<PostContact> Contacts(string text, bool uzbekSource)
    {
        var contacts = new List<PostContact>();
        foreach (Match match in Phone().Matches(text))
        {
            var raw = match.Value.Trim();
            var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
            if (raw.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
            else if (!raw.StartsWith('+') && digits.Length == 9 && uzbekSource && UzbekLocal().IsMatch(digits)) digits = "998" + digits;
            else if (!raw.StartsWith('+') && !(digits.Length == 12 && digits.StartsWith("998", StringComparison.Ordinal))) continue;
            if (digits.Length is >= 8 and <= 15 && digits[0] != '0')
                contacts.Add(new PostContact { Kind = ContactKind.Phone, Value = "+" + digits, Raw = raw });
        }
        contacts.AddRange(Email().Matches(text).Cast<Match>().Select(match => new PostContact
        { Kind = ContactKind.Email, Value = match.Value.ToLowerInvariant(), Raw = match.Value }));
        return contacts.DistinctBy(contact => (contact.Kind, contact.Value)).ToList();
    }

    internal static PostLanguage Language(string text, string? locale)
    {
        var cyrl = text.Count(character => character is >= '\u0400' and <= '\u052f');
        var latn = text.Count(character => char.IsAsciiLetter(character));
        var explicitCode = locale?.Split('-', '_')[0].ToLowerInvariant();
        if (explicitCode is "uz" or "ru" or "en")
            return new() { Code = explicitCode, Script = cyrl > latn ? "Cyrl" : "Latn", Confidence = 1, Mixed = cyrl > 5 && latn > 5 };
        var normalized = text.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        if (normalized.IndexOfAny(['ў', 'қ', 'ғ', 'ҳ']) >= 0 || UzbekWords().IsMatch(normalized))
            return new() { Code = "uz", Script = cyrl > latn ? "Cyrl" : "Latn", Confidence = 0.8, Mixed = cyrl > 5 && latn > 5 };
        if (cyrl > 0 && cyrl > latn)
            return new() { Code = "ru", Script = "Cyrl", Confidence = 0.6, Mixed = latn > 5 };
        if (EnglishWords().Matches(normalized).Count >= 2)
            return new() { Code = "en", Script = "Latn", Confidence = 0.6, Mixed = cyrl > 5 };
        return new() { Code = "und", Script = latn > 0 ? "Latn" : cyrl > 0 ? "Cyrl" : null };
    }

    [GeneratedRegex(@"^(?<year>\d{4})(?:-(?<month>\d{2})(?:-(?<day>\d{2})(?:T(?<hour>\d{2})(?::(?<minute>\d{2})(?::(?<second>\d{2})(?:\.\d+)?)?)?(?:Z|[+-]\d{2}:\d{2})?)?)?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDate();
    [GeneratedRegex(@"(?<!\d)\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}(?::\d{2})?(?:Z|[+-]\d{2}:\d{2})?)?(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex TextDate();
    [GeneratedRegex(@"(?<!\d)(?<day>\d{2})\.(?<month>\d{2})\.(?<year>\d{4})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex EuropeanDate();
    [GeneratedRegex(@"(?<![\d+])(?<amount>\d+(?:[ \u00a0]\d{3})*(?:[.,]\d{1,3})?)\s*(?<currency>UZS|USD|EUR|so['‘’ʻ`]?m|сўм|сум|\$|€)(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Price();
    [GeneratedRegex(@"\b(?:free|bepul|бесплатно)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Free();
    [GeneratedRegex(@"(?<![\w\d])(?:\+|00)?\d[\d ()\-]{6,}\d(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex Phone();
    [GeneratedRegex(@"^(?:20|33|50|55|61|62|65|66|67|69|70|71|72|73|74|75|76|77|78|79|88|90|91|93|94|95|97|98|99)\d{7}$", RegexOptions.CultureInvariant)]
    private static partial Regex UzbekLocal();
    [GeneratedRegex(@"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Email();
    [GeneratedRegex(@"\b(?:uchun|bilan|tadbir|narxi|toshkent|bo['‘’ʻ`]ladi|ko['‘’ʻ`]rgazma)\b", RegexOptions.CultureInvariant)]
    private static partial Regex UzbekWords();
    [GeneratedRegex(@"\b(?:the|and|with|from|event|tickets|this|will)\b", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishWords();
}
