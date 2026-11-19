using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Checks AI answers against the text the model read: every quote must appear there verbatim, on whole tokens,
/// and state the value it backs. <c>rental-terms@2</c> rejects a whole answer on one bad claim; Layer 2 extraction keeps
/// the rest of its answer and turns only the rejected field into unknown with a flag.</summary>
internal static partial class RentalTermEvidence
{
    private const RegexOptions PatternOptions = RegexOptions.CultureInvariant;
    private const string Number = @"[+-]?(?:[0-9]{1,3}(?:\s[0-9]{3})+|[0-9]+)(?:[.,][0-9]+)?";
    private const string WrittenNumber = @"(?:один|одна|одну|одного|одной|месячной|два|две|двух|три|тр[её]х|bir|birta|бир|ikki|икки|uch|уч|yarim|ярим|one|two|three|half)";
    private const string TimeUnit = @"(?:ноч(?:ь|и|ей)|сут(?:ки|ок)|день|дня|дней|недел[яиюь]|месяц(?:а|ев)?|год(?:а|ов)?|лет|(?:tun|kun|hafta|oy|yil)(?:ga)?|(?:тун|кун|ҳафта|ой|йил)(?:га)?|nights?|days?|weeks?|months?|years?)";
    private const string ExplicitCurrency = @"(?:usd|доллар(?:ов|а)?|uzs|сум(?:ов|а)?|сўм|so'm|eur|евро|rub|kzt|[$€₽₸])";
    // Conventional units (у.е., у. е., y.e., уе) are how Tashkent ads write US dollars. Layer 2 reads them as USD
    // (CheckTerms); rental-terms@2 (Parse) keeps its pinned rule that a claim of USD for "500 у.е." is invented.
    private const string ConventionalUnit = @"(?:[уy]\.\s?[еe]\.?|уе)";
    private const string CurrencyOrUnit = @"(?:" + ExplicitCurrency + "|" + ConventionalUnit + ")";
    // A scale word may carry one Uzbek case suffix (800 mingdan, 3 mlrdga); the suffix is part of the scaled amount.
    private const string UzbekCaseSuffix = @"(?:gacha|ning|dan|lik|ga|da|ni|гача|нинг|дан|лик|га|да|ни)";
    // Month names: Uzbek case suffixes after a Latin or Cyrillic stem (dekabrda, декабрга) and Russian endings (декабря).
    private const string MonthLatinSuffix = @"(?:igacha|gacha|idagi|dagi|idan|dan|ning|iga|ga|ida|da|ni|lik|si|gi|i)?";
    private const string MonthCyrillicSuffix = @"(?:игача|гача|идаги|даги|идан|дан|нинг|ига|га|ида|да|ни|лик|си|ги|ь|я|е|ю|й|а|у|ем|ом|и)?";
    private const string UtilityWord = @"(?:комм?унал[а-я]*|квартплат[а-я]*|komm?unal\w*|kamunal\w*)";

    internal static (RentalTermsAnswer? Answer, string? Reason) Parse(string? output, string description, int visibleLength)
    {
        if (string.IsNullOrWhiteSpace(output))
            return (null, "The model returned an empty answer.");

        RentalTermsAnswer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<RentalTermsAnswer>(output, EvidenceWire.Options);
        }
        catch (JsonException)
        {
            return (null, "The model returned an invalid rental-terms document.");
        }

        if (answer is null)
            return (null, "The model returned an invalid rental-terms document.");
        if (!VerifyDeposit(answer.Deposit, description, visibleLength, conventionalUnits: false))
            return (null, "Deposit claims did not match the description.");
        if (!VerifyMinimumStay(answer.MinimumStay, description, visibleLength))
            return (null, "Minimum-stay claims did not match the description.");
        if (!VerifyAdvanceRent(answer.AdvanceRent, description, visibleLength, conventionalUnits: false))
            return (null, "Advance-rent claims did not match the description.");
        if (!VerifyUtilities(answer.Utilities, description, visibleLength))
            return (null, "Utilities claims did not match the description.");

        return (answer, null);
    }

    /// <summary>Checks each term of <paramref name="answer"/> on its own: a term whose claims fail becomes unknown, and
    /// its field name (<c>deposit</c>, <c>minimumStay</c>, <c>advanceRent</c>, <c>utilities</c>) is listed as rejected.</summary>
    /// <param name="answer">The model's terms.</param>
    /// <param name="description">The text the quotes must come from.</param>
    /// <param name="visibleLength">How much of <paramref name="description"/> the model read.</param>
    internal static (RentalTermsAnswer Answer, IReadOnlyList<string> Rejected) CheckTerms(RentalTermsAnswer answer, string description, int visibleLength)
    {
        var rejected = new List<string>();
        var deposit = Keep(answer.Deposit, VerifyDeposit(answer.Deposit, description, visibleLength, conventionalUnits: true), "deposit", rejected);
        var minimum = Keep(answer.MinimumStay, VerifyMinimumStay(answer.MinimumStay, description, visibleLength), "minimumStay", rejected);
        var advance = Keep(answer.AdvanceRent, VerifyAdvanceRent(answer.AdvanceRent, description, visibleLength, conventionalUnits: true), "advanceRent", rejected);
        var utilities = Keep(answer.Utilities, VerifyUtilities(answer.Utilities, description, visibleLength), "utilities", rejected);
        return (new RentalTermsAnswer(deposit, minimum, advance, utilities), rejected);
    }

    /// <summary>Whether <paramref name="evidence"/> appears verbatim in the first <paramref name="visibleLength"/>
    /// characters of <paramref name="text"/>, starting and ending on token boundaries: the check every quote passes.</summary>
    internal static bool Quoted(string? evidence, string text, int visibleLength)
    {
        if (string.IsNullOrWhiteSpace(evidence) || evidence.Length > text.Length)
            return false;
        int offset = text.IndexOf(evidence, 0, Math.Min(visibleLength, text.Length), StringComparison.Ordinal);
        return offset >= 0 && HasWholeTokens(text, offset, evidence.Length);
    }

    /// <summary>Whether <paramref name="quote"/> states <paramref name="amount"/>: one of its numbers, scaled by a following
    /// thousand or million word (<c>тыс</c>, <c>ming</c>, <c>млн</c>, <c>k</c>), equals it.</summary>
    internal static bool StatesAmount(string quote, decimal amount)
    {
        string normalized = Normalize(quote);
        foreach (Match number in Quantity().Matches(normalized))
        {
            if (CountOf(number.Value) is not { } count)
                continue;
            var scale = MoneyScale().Match(normalized[(number.Index + number.Length)..]);
            decimal multiplier = ScaleOf(scale.Groups["scale"].Value);
            if (count == amount || (multiplier != 1m && Math.Abs(count) <= decimal.MaxValue / multiplier && count * multiplier == amount))
                return true;
        }
        return false;
    }

    /// <summary>Whether <paramref name="quote"/> states the date <paramref name="value"/>, an ISO date at
    /// <paramref name="precision"/>: a number equal to its year (a year), or to its month or a month name in Uzbek, Russian or
    /// English (a month), or to its day, its month number or a month name (a day or finer). A light check: it reads no
    /// weekday, relative phrase or spelled-out number, and it does not read the quote as a whole sentence.</summary>
    internal static bool StatesDate(string quote, string value, DatePrecision precision)
    {
        if (value.Length < 4 || !int.TryParse(value.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int year))
            return false;
        int? month = value.Length >= 7 && int.TryParse(value.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedMonth)
            ? parsedMonth : null;
        int? day = value.Length >= 10 && int.TryParse(value.AsSpan(8, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedDay)
            ? parsedDay : null;

        string normalized = Normalize(quote);
        var numbers = new HashSet<int>();
        foreach (Match number in QuoteNumber().Matches(normalized))
            numbers.Add(int.Parse(number.Value, NumberStyles.None, CultureInfo.InvariantCulture));
        bool Month(int expected) => numbers.Contains(expected) || NamesMonth(normalized, expected);

        return precision switch
        {
            DatePrecision.Year => numbers.Contains(year),
            DatePrecision.Month => month is { } expected && Month(expected),
            _ => (day is { } stated && numbers.Contains(stated)) || (month is { } expected && Month(expected)),
        };
    }

    private static bool NamesMonth(string normalized, int month)
    {
        string group = "m" + month.ToString(CultureInfo.InvariantCulture);
        foreach (Match word in MonthName().Matches(normalized))
            if (word.Groups[group].Success)
                return true;
        return false;
    }

    private static RentalTerm<T> Keep<T>(RentalTerm<T>? term, bool verified, string field, List<string> rejected) where T : class
    {
        if (verified && term is not null)
            return term;
        rejected.Add(field);
        return RentalTerm<T>.Unknown;
    }

    private static bool VerifyDeposit(RentalTerm<RentalDeposit>? term, string description, int visibleLength, bool conventionalUnits) =>
        Verify(term, description, visibleLength, DepositCue(), (value, quote, cue) => Deposit(value, quote, cue, conventionalUnits));

    private static bool VerifyMinimumStay(RentalTerm<RentalDuration>? term, string description, int visibleLength) =>
        Verify(term, description, visibleLength, MinimumCue(), Duration);

    private static bool VerifyAdvanceRent(RentalTerm<RentalAdvance>? term, string description, int visibleLength, bool conventionalUnits) =>
        Verify(term, description, visibleLength, AdvanceCue(), (value, quote, cue) => Advance(value, quote, cue, conventionalUnits));

    private static bool VerifyUtilities(RentalTerm<RentalUtilities>? term, string description, int visibleLength) =>
        Verify(term, description, visibleLength, UtilitiesCue(), Utilities, ambiguousExclusion: true);

    private static bool Verify<T>(RentalTerm<T>? term, string description, int visibleLength, Regex cue, Func<T, string, Regex, bool> agrees,
        bool ambiguousExclusion = false)
        where T : class
    {
        if (term?.Claims is null)
            return false;
        if (term.Status == RentalTermStatus.Unknown)
            return term.Claims.Count == 0;
        if (term.Claims.Count == 0)
            return false;

        var distinctValues = new HashSet<T?>();
        foreach (var claim in term.Claims)
        {
            if (string.IsNullOrWhiteSpace(claim?.Evidence) || !Quoted(claim.Evidence, description, visibleLength))
                return false;
            // The quote fits in the model's input. Token and clause checks use the saved description.
            int offset = description.IndexOf(claim.Evidence, 0, Math.Min(visibleLength, description.Length), StringComparison.Ordinal);
            string quote = Normalize(claim.Evidence);
            if (!cue.IsMatch(quote))
                return false;
            string clause = Normalize(Clause(description, offset, claim.Evidence.Length));
            string statement = Normalize(Clause(description, offset, claim.Evidence.Length, stopAtComma: false));
            if (Uncertain().IsMatch(statement) || (claim.Value is null ? !Negated(cue, quote, ambiguousExclusion)
                : Negated(cue, clause, ambiguousExclusion) || !agrees(claim.Value, quote, cue)
                    || !agrees(claim.Value, ambiguousExclusion ? statement : clause, cue)))
                return false;
            if (!distinctValues.Add(claim.Value))
                return false;
        }

        return term.Status switch
        {
            RentalTermStatus.Present => distinctValues.Count == 1 && !distinctValues.Contains(null),
            RentalTermStatus.Absent => distinctValues.Count == 1 && distinctValues.Contains(null),
            RentalTermStatus.Conflict => distinctValues.Count >= 2,
            _ => false,
        };
    }

    private static bool Deposit(RentalDeposit value, string quote, Regex cue, bool conventionalUnits) =>
        Payment(value.Amount, value.Currency, value.RentMultiple, quote, cue, conventionalUnits);

    private static bool Advance(RentalAdvance value, string quote, Regex cue, bool conventionalUnits) => value.Duration is { } duration
        ? value.Amount is null && value.Currency is null && value.RentMultiple is null && Duration(duration, quote, cue)
        : Payment(value.Amount, value.Currency, value.RentMultiple, quote, cue, conventionalUnits);

    private static bool Payment(decimal? amount, string? currency, decimal? rentMultiple, string quote, Regex cue, bool conventionalUnits)
    {
        var currencyName = conventionalUnits ? CurrencyOrUnitName() : CurrencyName();
        if (amount is null && currency is null && rentMultiple is null)
            return !Quantity().IsMatch(quote) && !currencyName.IsMatch(quote) && !DurationNumber().IsMatch(quote)
                && !RentMultiple().IsMatch(quote) && RequiredPayment().Matches(quote).Any(requirement => Connected(quote, cue, requirement));
        if (rentMultiple is > 0 && amount is null && currency is null)
        {
            var times = RentMultiple().Matches(quote);
            return times.Count == 1 && Quantity().Matches(quote).Count <= 1
                && CountOf(times[0].Groups["count"].Value) == rentMultiple && Connected(quote, cue, times[0]);
        }

        if (amount is not > 0 || rentMultiple is not null || DurationNumber().IsMatch(quote))
            return false;
        var numbers = Quantity().Matches(quote);
        var currencies = currencyName.Matches(quote);
        return numbers.Count == 1 && MatchesMoney(quote, cue, numbers[0], currencies, amount.Value, currency);
    }

    private static bool MatchesMoney(string quote, Regex cue, Match number, MatchCollection currencies, decimal expected, string? currency)
    {
        int end = number.Index + number.Length;
        var scale = MoneyScale().Match(quote[end..]);
        string token = scale.Groups["scale"].Value;
        decimal multiplier = ScaleOf(token);
        int scaledEnd = end + scale.Length;
        if (CountOf(number.Value) is not { } count || count <= 0m || count > decimal.MaxValue / multiplier || count * multiplier != expected
            || !Connected(quote, cue, number.Index, scaledEnd))
            return false;

        if (currencies.Count == 0)
            return currency is null;
        if (currencies.Count != 1 || CurrencyOf(currencies[0].Value) != currency)
            return false;
        var stated = currencies[0];
        return string.IsNullOrWhiteSpace(Between(quote, number.Index, scaledEnd, stated.Index, stated.Index + stated.Length));
    }

    private static bool Duration(RentalDuration duration, string quote, Regex cue)
    {
        var terms = DurationNumber().Matches(quote);
        return duration.Count > 0 && terms.Count == 1 && Quantity().Matches(quote).Count <= 1
            && CountOf(terms[0].Groups["count"].Value) == duration.Count
            && UnitOf(terms[0].Groups["unit"].Value) == duration.Unit && Connected(quote, cue, terms[0]);
    }

    private static bool Utilities(RentalUtilities utilities, string quote, Regex cue)
    {
        if (InclusionOutsideRent().IsMatch(quote))
            return false;
        bool separate = UtilitySeparate().Matches(quote).Any(value => Connected(quote, cue, value));
        bool included = UtilityIncluded().Matches(quote).Any(value => Connected(quote, cue, value));
        bool partial = UtilityPartial().IsMatch(quote);
        return utilities.Billing switch
        {
            RentalUtilitiesBilling.Included => included && !separate && !partial && !UtilityException().IsMatch(quote),
            RentalUtilitiesBilling.Separate => separate && !partial,
            RentalUtilitiesBilling.Partial => included && partial,
            _ => false,
        };
    }

    private static bool Connected(string quote, Regex cue, Match value) =>
        Connected(quote, cue, value.Index, value.Index + value.Length);

    private static bool Connected(string quote, Regex cue, int start, int end)
    {
        foreach (Match label in cue.Matches(quote))
        {
            // A local value may be before or after the label. Reject a number or modifier from a nearby different term.
            string gap = Between(quote, label.Index, label.Index + label.Length, start, end);
            if (Connector().IsMatch(CurrencyName().Replace(gap, string.Empty)))
                return true;
        }
        return false;
    }

    private static string Between(string text, int firstStart, int firstEnd, int secondStart, int secondEnd)
    {
        int start = Math.Min(firstEnd, secondEnd);
        int end = Math.Max(firstStart, secondStart);
        return start < end ? text[start..end] : string.Empty;
    }

    private static bool HasWholeTokens(string text, int start, int length)
    {
        int end = start + length;
        return (start == 0 || !SplitsToken(text, start))
            && (end == text.Length || !SplitsToken(text, end));
    }

    private static bool SplitsToken(string text, int boundary) =>
        TokenCharacter(text[boundary - 1]) && TokenCharacter(text[boundary])
        || (text[boundary] is '.' or ',' && NumericPunctuation(text, boundary))
        || (text[boundary - 1] is '.' or ',' && NumericPunctuation(text, boundary - 1));

    private static bool TokenCharacter(char character) => char.IsLetterOrDigit(character)
        || char.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;

    private static string Clause(string text, int start, int length, bool stopAtComma = true)
    {
        int left = start;
        int right = start + length;
        while (left > 0 && !ClauseBreak(text, left - 1, stopAtComma))
            left--;
        while (right < text.Length && !ClauseBreak(text, right, stopAtComma))
            right++;
        return text[left..right];
    }

    private static bool ClauseBreak(string text, int index, bool stopAtComma) => text[index] switch
    {
        '.' => !NumericPunctuation(text, index),
        ',' => stopAtComma && !NumericPunctuation(text, index),
        ';' or '!' or '?' or '\n' or '\r' => true,
        _ => false,
    };

    private static bool NumericPunctuation(string text, int index) =>
        index > 0 && index + 1 < text.Length && char.IsAsciiDigit(text[index - 1]) && char.IsAsciiDigit(text[index + 1]);

    private static bool Negated(Regex cue, string text, bool ambiguousExclusion)
    {
        foreach (Match occurrence in cue.Matches(text))
        {
            string before = text[..occurrence.Index];
            string after = text[(occurrence.Index + occurrence.Length)..];
            var prefix = AbsenceBefore().Match(before);
            if (prefix.Success && (!ambiguousExclusion || prefix.Groups["word"].Value is not ("без" or "without"))
                || AbsenceAfter().IsMatch(after))
                return true;
        }
        return false;
    }

    private static string Normalize(string raw) =>
        raw.ToLowerInvariant().Replace('ё', 'е').Replace('’', '\'').Replace('‘', '\'').Replace('ʻ', '\'').Replace('ʼ', '\'').Replace('`', '\'');

    private static decimal? CountOf(string raw)
    {
        if (AmbiguousNumberSeparator().IsMatch(raw))
            return null;
        string number = Space().Replace(raw, string.Empty).Replace(',', '.');
        if (decimal.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return number switch
        {
            "один" or "одна" or "одну" or "одного" or "одной" or "месячной" or "bir" or "birta" or "бир" or "one" => 1,
            "два" or "две" or "двух" or "ikki" or "икки" or "two" => 2,
            "три" or "трех" or "uch" or "уч" or "three" => 3,
            "yarim" or "ярим" or "half" => 0.5m,
            _ => null,
        };
    }

    private static RentalDurationUnit? UnitOf(string label) => (label.EndsWith("ga", StringComparison.Ordinal)
        || label.EndsWith("га", StringComparison.Ordinal) ? label[..^2] : label) switch
    {
        "ночь" or "ночи" or "ночей" or "tun" or "тун" or "night" or "nights" => RentalDurationUnit.Night,
        "сутки" or "суток" or "день" or "дня" or "дней" or "kun" or "кун" or "day" or "days" => RentalDurationUnit.Day,
        "неделя" or "неделю" or "недели" or "недель" or "hafta" or "ҳафта" or "week" or "weeks" => RentalDurationUnit.Week,
        "месяц" or "месяца" or "месяцев" or "oy" or "ой" or "month" or "months" => RentalDurationUnit.Month,
        "год" or "года" or "годов" or "лет" or "yil" or "йил" or "year" or "years" => RentalDurationUnit.Year,
        _ => null,
    };

    private static string CurrencyOf(string token) => token switch
    {
        "$" or "usd" or "доллар" or "долларов" or "доллара" => "USD",
        "€" or "eur" or "евро" => "EUR",
        "₽" or "rub" => "RUB",
        "₸" or "kzt" => "KZT",
        _ when ConventionalUnitOnly().IsMatch(token) => "USD",
        _ => "UZS",
    };

    private static decimal ScaleOf(string word) => word switch
    {
        "" => 1m,
        "milliard" or "mlrd" or "миллиард" or "миллиарда" or "миллиардов" or "млрд" => 1_000_000_000m,
        "million" or "mln" or "миллион" or "миллиона" or "миллионов" or "млн" => 1_000_000m,
        _ => 1_000m,
    };

    [GeneratedRegex(@"\s+", PatternOptions)]
    private static partial Regex Space();

    [GeneratedRegex(@"^[\s:=()\-–—]*(?:(?:в\s+(?:размере|сумме|виде)|в\s+цену|за|составля[а-я]*|обязател[а-я]*|аренды|проживания|услуг[а-я]*|платеж[а-я]*|оплачива[а-я]*|частично|часть|of|is|for|included\s+in\s+the\s+price|charges|partially|are|puli|пули|to'lov[а-яa-z]*|тўлов[а-я]*|narxga|нархга|qisman|қисман)(?:[\s:=()\-–—]+|$))*$", PatternOptions)]
    private static partial Regex Connector();

    [GeneratedRegex(@"\b(?:возможно|вероятно|может\s+потребоваться|если|только\s+(?:при|для)|в\s+зависимости\s+от|possibly|if|maybe|only\s+(?:for|when)|depending\s+on|ehtimol|agar|эҳтимол|агар)\b", PatternOptions)]
    private static partial Regex Uncertain();

    [GeneratedRegex(@"\b(?:обязател(?:ен|ьна|ьно|ьны)|требуется|необходим(?:а|о|ы)?|нужен|нужна|required|mandatory|compulsory|must\s+be\s+paid|shart|majburiy|talab\s+qilinadi|шарт|мажбурий|талаб\s+қилинади)\b", PatternOptions)]
    private static partial Regex RequiredPayment();

    [GeneratedRegex(@"(?<![\p{L}\d])" + Number + @"(?![\d]|[.,]\d)", PatternOptions)]
    private static partial Regex Quantity();

    [GeneratedRegex(@"^[+-]?[1-9][0-9]{0,2}[.,][0-9]{3}$", PatternOptions)]
    private static partial Regex AmbiguousNumberSeparator();

    [GeneratedRegex(@"(?<!\p{L})" + ExplicitCurrency + @"(?!\p{L})", PatternOptions)]
    private static partial Regex CurrencyName();

    [GeneratedRegex(@"(?<!\p{L})" + CurrencyOrUnit + @"(?!\p{L})", PatternOptions)]
    private static partial Regex CurrencyOrUnitName();

    [GeneratedRegex("^" + ConventionalUnit + "$", PatternOptions)]
    private static partial Regex ConventionalUnitOnly();

    [GeneratedRegex(@"(?<![0-9])[0-9]{1,4}(?![0-9])", PatternOptions)]
    private static partial Regex QuoteNumber();

    [GeneratedRegex(@"(?<!\p{L})(?:"
        + @"(?<m1>yanvar" + MonthLatinSuffix + "|january|jan|январ" + MonthCyrillicSuffix + "|янв)"
        + @"|(?<m2>fevral" + MonthLatinSuffix + "|february|feb|феврал" + MonthCyrillicSuffix + "|фев)"
        + @"|(?<m3>mart" + MonthLatinSuffix + "|march|mar|март" + MonthCyrillicSuffix + "|мар)"
        + @"|(?<m4>aprel" + MonthLatinSuffix + "|april|apr|апрел" + MonthCyrillicSuffix + "|апр)"
        + @"|(?<m5>may" + MonthLatinSuffix + "|май" + MonthCyrillicSuffix + "|мая|мае|маю)"
        + @"|(?<m6>iyun" + MonthLatinSuffix + "|june|jun|июн" + MonthCyrillicSuffix + ")"
        + @"|(?<m7>iyul" + MonthLatinSuffix + "|july|jul|июл" + MonthCyrillicSuffix + ")"
        + @"|(?<m8>avgust" + MonthLatinSuffix + "|august|aug|август" + MonthCyrillicSuffix + "|авг)"
        + @"|(?<m9>sent(?:y)?abr" + MonthLatinSuffix + "|september|sept|sep|сент(?:я)?бр" + MonthCyrillicSuffix + "|сент|сен)"
        + @"|(?<m10>okt(?:y)?abr" + MonthLatinSuffix + "|october|oct|окт(?:я)?бр" + MonthCyrillicSuffix + "|окт)"
        + @"|(?<m11>noyabr" + MonthLatinSuffix + "|november|nov|ноябр" + MonthCyrillicSuffix + "|ноя)"
        + @"|(?<m12>dekabr" + MonthLatinSuffix + "|december|dec|декабр" + MonthCyrillicSuffix + "|дек)"
        + @")(?!\p{L})", PatternOptions)]
    private static partial Regex MonthName();

    [GeneratedRegex(@"^\s*(?:(?<scale>k)|(?<scale>тыс(?:яч(?:а|и)?)?|млрд|млн|миллиард(?:а|ов)?|миллион(?:а|ов)?|ming|минг|milliard|million|mlrd|mln)(?:[-']?" + UzbekCaseSuffix + @")?)\b\.?", PatternOptions)]
    private static partial Regex MoneyScale();

    [GeneratedRegex(@"(?<![\p{L}\d])(?<count>" + Number + "|" + WrittenNumber + @")\s*(?:-\s*)?(?<unit>" + TimeUnit + @")(?!\p{L})", PatternOptions)]
    private static partial Regex DurationNumber();

    [GeneratedRegex(@"(?<![\p{L}\d])(?<count>" + Number + "|" + WrittenNumber + @")\s*(?:(?:x|×|раз(?:а)?|times)\s*(?:rent|аренд[а-я]*|ijara|ижара)|(?:месяц(?:а|ев)?|месячн[а-я]*)\s+(?:арендной\s+платы|плат[а-я]*|аренд[а-я]*|оплат[а-я]*)|(?:oylik\s+ijara|ойлик\s+ижара|month(?:'s|s')?\s+rent))|(?<count>месячной)\s+(?:арендной\s+платы|аренды|платы|оплаты)", PatternOptions)]
    private static partial Regex RentMultiple();

    [GeneratedRegex(@"\b(?:залог[а-я]*|депозит[а-я]*|depozit\w*|garov\w*|гаров\w*|deposit)\b", PatternOptions)]
    private static partial Regex DepositCue();

    [GeneratedRegex(@"\b(?:минимум|минимальн[а-я]*(?:\s+срок[а-я]*)?|не\s+менее|kamida|minimal\s+muddat|камида|минимал\s+муддат|minimum(?:\s+stay)?|at\s+least|от(?=\s+(?:" + Number + "|" + WrittenNumber + @")\s+" + TimeUnit + @"\b))\b", PatternOptions)]
    private static partial Regex MinimumCue();

    [GeneratedRegex(@"\b(?:предоплат[а-я]*|аванс[а-я]*|оплат[а-я]*\s+вперед|oldindan\s+(?:to'lov|to'lanadi)|олдиндан\s+(?:тўлов|тўланади)|advance\s+(?:rent|payment)|prepaid\s+rent)\b", PatternOptions)]
    private static partial Regex AdvanceCue();

    [GeneratedRegex(@"\b(?:" + UtilityWord + @"|utility|utilities)\b", PatternOptions)]
    private static partial Regex UtilitiesCue();

    [GeneratedRegex(@"\b(?<word>без|нет|no|without)\s+(?:отдельн[а-я]*\s+)?$", PatternOptions)]
    private static partial Regex AbsenceBefore();

    [GeneratedRegex(@"^\s*[:=]?\s*(?:(?:" + Number + @")\s*(?:" + CurrencyOrUnit + "|" + TimeUnit + @")?\s+)?(?:не\s+(?:нуж[а-я]*|требу[а-я]*|берется|взимается|предусмотр[а-я]*)|нет|отсутств[а-я]*|отмен[а-я]*|yo'q|йўқ|olinmaydi|олинмайди|talab\s+qilinmaydi|талаб\s+қилинмайди|shart\s+emas|шарт\s+эмас|none|not\s+required|waived|0(?:[.,]0+)?)(?![\p{L}\d]|[.,]\d)", PatternOptions)]
    private static partial Regex AbsenceAfter();

    [GeneratedRegex(@"\b(?:не\s+включ[а-я]*|(?:совсем\s+)?отдельно|not\s+included|separately|alohida|алоҳида|kiritilmagan|киритилмаган)\b|\+\s*(?:" + UtilityWord + @"|utilities)", PatternOptions)]
    private static partial Regex UtilitySeparate();

    [GeneratedRegex(@"\b(?:включ[а-я]*|вход[а-я]*\s+в\s+(?:цену|стоимость|аренду)|included|kiritilgan|киритилган|ichida|ичида)\b", PatternOptions)]
    private static partial Regex UtilityIncluded();

    [GeneratedRegex(@"\b(?:included\s+(?:in|into)\s+(?!(?:the\s+)?(?:rent|price|(?:monthly|daily)\s+rent|asking\s+price)\b)\p{L}+|включ[а-я]*\s+в\s+(?!(?:(?:общую|месячную|суточную)\s+)?(?:цену|стоимость|аренду|арендную\s+плату)\b)\p{L}+|(?:(?:in|into)\s+(?:the\s+)?(?:deposit|advance(?:\s+rent|\s+payment)?)|в\s+(?:залог[а-я]*|депозит[а-я]*|предоплат[а-я]*))\s+(?:\p{L}+\s+){0,3}(?:включ[а-я]*|included))\b", PatternOptions)]
    private static partial Regex InclusionOutsideRent();

    [GeneratedRegex(@"\b(?:except|кроме|за\s+исключением|bundan\s+tashqari|бундан\s+ташқари)\b", PatternOptions)]
    private static partial Regex UtilityException();

    [GeneratedRegex(@"\b(?:частично|partially|qisman|қисман)\b", PatternOptions)]
    private static partial Regex UtilityPartial();
}
