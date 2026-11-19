using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Turns an entity's name into the key matching compares: Russian, Uzbek Latin, Uzbek Cyrillic and English forms
/// of one name collapse to one key, without years or ordinals.</summary>
/// <remarks>
///   - Cyrillic is transliterated to Uzbek Latin; apostrophes (<c>oʻ</c>, <c>gʻ</c>) drop, <c>kh</c> reads <c>x</c> and
///     <c>zh</c> reads <c>j</c>, so <c>Тошкент</c> and <c>Toshkent</c> agree
///   - years, numbers, ordinals (<c>9th</c>, <c>9-chi</c>, <c>9-я</c>, <c>IX</c>) and stopwords drop
///   - a small glossary maps the event and place words the sources use to English: <c>moda haftaligi</c>,
///     <c>неделя моды</c> and <c>fashion week</c> all read <c>fashion week</c>; city stems (<c>Ташкентская</c>,
///     <c>Toshkentda</c>) read the city
///   - the words are sorted, so word order never matters: <c>Неделя моды в Ташкенте</c> and <c>Tashkent Fashion Week</c>
///     share the key <c>fashion tashkent week</c>
///   - names the glossary does not cover still match by trigram similarity in the store
/// </remarks>
internal static partial class EntityNames
{
    private const int MaxKey = 300;

    private static readonly IReadOnlyDictionary<char, string> Cyrillic = new Dictionary<char, string>
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "yo", ['ж'] = "j", ['з'] = "z",
        ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r",
        ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "x", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sh",
        ['ъ'] = "", ['ы'] = "i", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya", ['ў'] = "o", ['қ'] = "q", ['ғ'] = "g",
        ['ҳ'] = "h", ['і'] = "i", ['є'] = "e", ['ї'] = "i",
    };

    private static readonly IReadOnlySet<string> Stopwords = new HashSet<string>(StringComparer.Ordinal)
    {
        "the", "of", "in", "at", "and", "for", "an", "on", "by", "to", "with",
        "v", "vo", "i", "na", "po", "dlya", "so", "k", "o", "ob", "iz", "ot", "do", "za", "pri",
        "va", "uchun", "bilan", "da", "ning", "ga",
    };

    /// <summary>Suffix words an ordinal leaves after its number: <c>9-chi</c>, <c>9-я</c>, <c>9th</c>.</summary>
    private static readonly IReadOnlySet<string> OrdinalSuffixes = new HashSet<string>(StringComparer.Ordinal)
    {
        "chi", "nchi", "inchi", "ya", "y", "iy", "yy", "yi", "oy", "go", "ye", "e", "th", "st", "nd", "rd",
    };

    private static readonly IReadOnlyDictionary<string, string> Glossary = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["moda"] = "fashion", ["modasi"] = "fashion", ["mody"] = "fashion", ["modi"] = "fashion", ["modnaya"] = "fashion",
        ["modniy"] = "fashion", ["modnoy"] = "fashion", ["modnix"] = "fashion",
        ["hafta"] = "week", ["haftasi"] = "week", ["haftalik"] = "week", ["haftaligi"] = "week", ["nedelya"] = "week",
        ["nedeli"] = "week", ["nedelyu"] = "week", ["weeks"] = "week",
        ["vistavka"] = "exhibition", ["vistavki"] = "exhibition", ["korgazma"] = "exhibition", ["korgazmasi"] = "exhibition",
        ["korgazmalari"] = "exhibition", ["exhibitions"] = "exhibition",
        ["yarmarka"] = "fair", ["yarmarkasi"] = "fair", ["yarmarki"] = "fair",
        ["festivali"] = "festival", ["festivalya"] = "festival", ["fest"] = "festival",
        ["forumi"] = "forum", ["foruma"] = "forum",
        ["konferentsiya"] = "conference", ["konferensiya"] = "conference", ["konferensiyasi"] = "conference",
        ["konferentsii"] = "conference", ["anjuman"] = "conference", ["anjumani"] = "conference",
        ["mejdunarodnaya"] = "international", ["mejdunarodniy"] = "international", ["mejdunarodnoy"] = "international",
        ["xalqaro"] = "international",
        ["tekstil"] = "textile", ["tekstilnaya"] = "textile", ["toqimachilik"] = "textile", ["textiles"] = "textile",
        ["dizayn"] = "design", ["dizayna"] = "design", ["dizayni"] = "design",
        ["konkurs"] = "competition", ["tanlov"] = "competition", ["tanlovi"] = "competition",
        ["ekspo"] = "expo", ["shou"] = "show",
        ["xiva"] = "khiva", ["xive"] = "khiva", ["xivada"] = "khiva",
    };

    /// <summary>Stems whose every inflection reads one place: <c>Ташкентская</c>, <c>Toshkentda</c>, <c>Tashkente</c>.</summary>
    private static readonly (string Stem, string Name)[] Stems =
    [
        ("tashkent", "tashkent"), ("toshkent", "tashkent"), ("samarkand", "samarkand"), ("samarqand", "samarkand"),
        ("buxar", "bukhara"), ("buxor", "bukhara"), ("andijan", "andijan"), ("andijon", "andijan"), ("namangan", "namangan"),
        ("fergan", "fergana"), ("fargon", "fergana"), ("uzbekist", "uzbekistan"), ("ozbekist", "uzbekistan"),
        ("karakalpak", "karakalpakstan"), ("qoraqalpog", "karakalpakstan"), ("nukus", "nukus"),
    ];

    /// <summary>Generic place words a district or city name carries in any language.</summary>
    private static readonly IReadOnlySet<string> PlaceWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "tumani", "tuman", "rayon", "rayona", "rayone", "district", "shahri", "shahar", "gorod", "g", "city", "viloyati", "viloyat",
        "oblast", "oblasti", "region", "mahalla", "massiv", "kvartal", "mikrorayon", "mavze",
    };

    /// <summary>Returns the matching key of a name: lowercase Latin words, sorted, without years, ordinals or stopwords;
    /// empty when nothing is left.</summary>
    /// <param name="name">The name in any of the sources' languages.</param>
    internal static string Key(string? name)
    {
        var words = Words(name);
        var kept = new SortedSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < words.Count; index++)
        {
            string word = words[index];
            if (IsNumber(word))
            {
                if (index + 1 < words.Count && OrdinalSuffixes.Contains(words[index + 1]))
                    index++;
                continue;
            }
            if (OrdinalNumber().IsMatch(word) || Roman().IsMatch(word) || word.Length < 2 || Stopwords.Contains(word))
                continue;
            kept.Add(Canonical(word));
        }
        string key = string.Join(' ', kept);
        return key.Length <= MaxKey ? key : key[..MaxKey].TrimEnd();
    }

    /// <summary>Returns the key of a place: <see cref="Key"/> without generic place words, with Uzbek and Russian vowel
    /// spellings folded (<c>o</c> reads <c>a</c>, <c>q</c> reads <c>k</c>), so <c>Yunusobod tumani</c> and
    /// <c>Юнусабадский район</c> compare by stem.</summary>
    /// <param name="place">The city or district.</param>
    internal static string PlaceKey(string? place)
    {
        var words = Key(place).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !PlaceWords.Contains(word))
            .Select(word => word.Replace('o', 'a').Replace('q', 'k'))
            .Order(StringComparer.Ordinal);
        return string.Join(' ', words);
    }

    /// <summary>Whether two place keys name one place: equal, or every word of the shorter one starts or is started by a
    /// word of the other, five letters or more; an empty key never matches.</summary>
    internal static bool SamePlace(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return false;
        if (left == right)
            return true;
        var a = left.Split(' ');
        var b = right.Split(' ');
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        return shorter.All(word => longer.Any(other => Prefixed(word, other)));
    }

    /// <summary>Returns the four-digit year a label or name states, or null.</summary>
    internal static int? Year(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;
        var match = YearPattern().Match(label);
        return match.Success ? int.Parse(match.Value, CultureInfo.InvariantCulture) : null;
    }

    private static bool Prefixed(string word, string other)
    {
        if (word == other)
            return true;
        var (shorter, longer) = word.Length <= other.Length ? (word, other) : (other, word);
        return shorter.Length >= 5 && longer.StartsWith(shorter, StringComparison.Ordinal);
    }

    private static List<string> Words(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return [];
        string text = name.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var latin = new StringBuilder(text.Length + 8);
        foreach (char letter in text)
        {
            if (Cyrillic.TryGetValue(letter, out var mapped))
                latin.Append(mapped);
            else if (letter is '\'' or '`' or '‘' or '’' or 'ʻ' or 'ʼ' or 'ʽ' or '´')
                continue;
            else
                latin.Append(letter);
        }
        string folded = RemoveMarks(latin.ToString());
        var words = new List<string>();
        foreach (Match match in Token().Matches(folded))
            words.Add(match.Value.Replace("kh", "x", StringComparison.Ordinal).Replace("zh", "j", StringComparison.Ordinal));
        return words;
    }

    private static string RemoveMarks(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(decomposed.Length);
        foreach (char letter in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark)
                plain.Append(letter);
        return plain.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string Canonical(string word)
    {
        if (Glossary.TryGetValue(word, out var english))
            return english;
        foreach (var (stem, place) in Stems)
            if (word.StartsWith(stem, StringComparison.Ordinal))
                return place;
        return word;
    }

    private static bool IsNumber(string word) => word.All(char.IsAsciiDigit);

    [GeneratedRegex(@"[\p{L}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex Token();

    [GeneratedRegex(@"^\d+(st|nd|rd|th|chi|nchi|inchi|y|ya|yi|iy|oy|go|ye|e)?$", RegexOptions.CultureInvariant)]
    private static partial Regex OrdinalNumber();

    [GeneratedRegex(@"^(?=[ivx]{2,})x{0,3}(ix|iv|v?i{0,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex Roman();

    [GeneratedRegex(@"(?<!\d)(19|20)\d{2}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex YearPattern();
}
