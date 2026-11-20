using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Replaces phone numbers in a post's words with <c>[phone]</c> before any prompt renders them, so no model call and no
/// stored prompt holds a number a person could call.</summary>
/// <remarks>
///   - one helper for every pass that renders a post's <c>title</c> and <c>text</c> (<c>post-tags</c>, <c>extract-event</c>,
///     <c>extract-listing</c>) and for the text its quotes are checked against, so a quote and its source agree
///   - a number with a plus sign (<c>+998 90 123-45-67</c>, <c>+7 (999) 123 45 67</c>, any <c>+</c> number of 7 to 15 digits; a
///     <c>+998</c> number is its first 12 digits), and a run of 9 to 12 digits with single separators between them
///     (<c>90 123 45 67</c>, <c>998 90 123 45 67</c>, <c>(90) 123-45-67</c>, <c>8 (900) 123-45-67</c>); a bare run counts when it
///     has 9 digits and starts with an Uzbek operator or city code, 11 and starts with 7 or 8, or 12 and starts with 998
///   - a 9-digit number whose first two digits are an Uzbek operator code (20, 33, 50, 55, 61, 62, 65, 66, 67, 69, 70 to 79, 88,
///     90, 91, 93, 94, 95, 97, 98, 99) is a phone with no cue word, written plain or grouped by spaces, hyphens, dots or
///     brackets, a thousand grouping included (<c>901234567</c>, <c>901 234 567</c>, <c>90.123.45.67</c>), unless a currency or unit word or symbol follows within 3 characters (<c>сум</c>, <c>so'm</c>,
///     <c>$</c>, <c>у.е.</c>, <c>м2</c>, <c>кв</c>) or the digits are a round price (<c>650 000 000</c>)
///   - what stays: prices in thousand groups (<c>150 000 000</c>, <c>1.200.000.000</c>) and bare prices with six or more trailing
///     zeros, a run followed by a currency or unit, years, areas, floors, dates, and a lone digit that follows a number
///     (<c>90 123 45 67 2 комнаты</c> keeps its <c>2</c>); any other thousand-grouped run is a phone only right after a word that
///     asks for a call (<c>тел</c>, <c>tel</c>, <c>whatsapp</c>, <c>звоните</c>)
///   - a heuristic: a run that fits none of these is left alone, and the output check still removes a contact from a stored value
/// </remarks>
internal static partial class PhoneScrub
{
    /// <summary>The text a phone number becomes.</summary>
    internal const string Placeholder = "[phone]";

    private const int MinRunDigits = 9;
    private const int MaxRunDigits = 12;
    private const int MinPlusDigits = 7;
    private const int MaxPlusDigits = 15;
    private const int UzbekDigits = 12;
    private const int CueWindow = 24;
    private const int QuantityWindow = 16;

    /// <summary>Returns <paramref name="text"/> with every phone number replaced by <see cref="Placeholder"/>; a text with none
    /// comes back unchanged, and null or empty comes back as it is.</summary>
    /// <param name="text">The post's title or text, or another line a prompt receives.</param>
    internal static string? Apply(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        string plus = Plus().Replace(text, ScrubPlus);
        return Run().Replace(plus, match => ScrubRun(match, plus));
    }

    private static string ScrubPlus(Match match)
    {
        var (core, tail) = TrimTail(match.Value);
        if (Digits(core) > UzbekDigits && DigitsOf(core).StartsWith("998", StringComparison.Ordinal))
        {
            core = CutDigits(core, UzbekDigits);
            tail = match.Value[core.Length..];
        }
        return Digits(core) is >= MinPlusDigits and <= MaxPlusDigits ? Placeholder + tail : match.Value;
    }

    private static string ScrubRun(Match match, string input)
    {
        string value = match.Value;
        var (core, tail) = TrimTail(value);
        if (Digits(core) > MaxRunDigits && DigitsOf(core).StartsWith("998", StringComparison.Ordinal))
        {
            core = CutDigits(core, UzbekDigits);
            tail = value[core.Length..];
        }
        return IsPhone(core, input, match.Index, match.Index + core.Length) ? Placeholder + tail : value;
    }

    private static bool IsPhone(string run, string input, int index, int end)
    {
        int digits = Digits(run);
        if (digits is < MinRunDigits or > MaxRunDigits)
            return false;
        if (AfterCue(input, index))
            return true;
        if (QuantityFollows(input, end))
            return false;

        string[] groups = Groups(run);
        if (groups.Length == 1)
            return BarePhone(groups[0]);
        if (ThousandGrouped(run) && !UzbekMobile(DigitsOf(run)))
            return false;
        return !DateLike().IsMatch(run) && !groups.All(group => group.Length == 4)
            && groups[^1].Length >= 2 && groups[0].Length <= 4 && groups.Length <= 6;
    }

    /// <summary>Whether the digits are a 9-digit Uzbek mobile or city number: an operator code, and not a round price.</summary>
    private static bool UzbekMobile(string digits) => digits.Length == 9 && OperatorCode().IsMatch(digits) && !PriceLike().IsMatch(digits);

    private static bool BarePhone(string digits) => digits.Length switch
    {
        9 => UzbekMobile(digits),
        11 => digits[0] is '7' or '8' && !PriceLike().IsMatch(digits),
        12 => digits.StartsWith("998", StringComparison.Ordinal),
        _ => false,
    };

    private static bool AfterCue(string input, int index)
    {
        int start = Math.Max(0, index - CueWindow);
        return Cue().IsMatch(input.AsSpan(start, index - start));
    }

    /// <summary>Whether a currency or unit word or symbol follows within 3 characters of the number: a quantity, not a phone.</summary>
    private static bool QuantityFollows(string input, int end) =>
        Quantity().IsMatch(input.AsSpan(end, Math.Min(QuantityWindow, input.Length - end)));

    private static bool ThousandGrouped(string value) => ThousandShape().IsMatch(value.Replace("(", "").Replace(")", "").Trim());

    /// <summary>Splits off the lone digits that trail a number (<c>90 123 45 67 2</c>) while 9 digits or more remain.</summary>
    private static (string Core, string Tail) TrimTail(string value)
    {
        string core = value;
        while (Digits(core) > MinRunDigits && TailGroup().Match(core) is { Success: true } tail && tail.Groups["d"].Length == 1)
            core = core[..tail.Index];
        return (core, value[core.Length..]);
    }

    private static string CutDigits(string value, int digits)
    {
        int seen = 0;
        for (int index = 0; index < value.Length; index++)
            if (char.IsAsciiDigit(value[index]) && ++seen == digits)
                return value[..(index + 1)];
        return value;
    }

    private static string[] Groups(string value) => NonDigits().Split(value).Where(group => group.Length > 0).ToArray();

    private static string DigitsOf(string value) => new(value.Where(char.IsAsciiDigit).ToArray());

    private static int Digits(string value) => value.Count(char.IsAsciiDigit);

    [GeneratedRegex(@"(?<![\w+])\+[ \t ]*\(?\d[\d \t \-.()]{5,20}\d", RegexOptions.CultureInvariant)]
    private static partial Regex Plus();

    // A digit run with at most two separator characters (space, tab, hyphen, dot, parentheses) between digits; never glued to a
    // longer number, a plus-number or a decimal.
    [GeneratedRegex(@"(?<![\d+])(?<![\d][.,])\(?\d(?:[ \t  \-.()]{0,2}\d){6,15}(?![\d]|[.,]\d)", RegexOptions.CultureInvariant)]
    private static partial Regex Run();

    [GeneratedRegex(@"[ \t  \-.()]*(?<d>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex TailGroup();

    [GeneratedRegex(@"^\d{1,3}(?:[   .,]\d{3})+$", RegexOptions.CultureInvariant)]
    private static partial Regex ThousandShape();

    [GeneratedRegex(@"^\d{1,2}[./\-]\d{1,2}[./\-](?:\d{4}|\d{2})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex DateLike();

    [GeneratedRegex(@"\D+", RegexOptions.CultureInvariant)]
    private static partial Regex NonDigits();

    // The first two digits of an Uzbek mobile or city number.
    [GeneratedRegex(@"^(?:20|33|50|55|6[125679]|7[0-9]|88|9[013-5789])\d{7}$", RegexOptions.CultureInvariant)]
    private static partial Regex OperatorCode();

    // A currency or unit that follows a number within 3 separator characters: сум, so'm, sum, uzs, у.е., $, usd, м2, м², кв.
    [GeneratedRegex(@"^[ \t\u00a0.,:/\-]{0,3}(?:(?:сум|сўм|so['’‘ʻʼ`]?m|sum|uzs|usd|долл|dollar|eur)\p{L}*|(?:у\.?\s?е|y\.?\s?e)(?!\p{L})|\$|€|[мm]\s?[2²]|(?:кв|kv|sqm)(?:\.|(?!\p{L})))",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Quantity();

    [GeneratedRegex(@"^\d{1,3}0{6,}$", RegexOptions.CultureInvariant)]
    private static partial Regex PriceLike();

    [GeneratedRegex(@"(?:тел|tel|phone|call|whats|watsap|vatsap|viber|telegram|телеграм|звон|связ|aloqa|номер|nomer|контакт|contact|\bwa|\bws)\p{L}*[\s:.\-–—()]*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Cue();
}
