using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

internal static partial class RentalTermEvidence
{
    private const RegexOptions PatternOptions = RegexOptions.CultureInvariant;
    private const string Number = @"[+-]?(?:[0-9]{1,3}(?:\s[0-9]{3})+|[0-9]+)(?:[.,][0-9]+)?";
    private const string WrittenNumber = @"(?:один|одна|одну|одного|одной|месячной|два|две|двух|три|тр[её]х|bir|birta|бир|ikki|икки|uch|уч|yarim|ярим|one|two|three|half)";
    private const string TimeUnit = @"(?:ноч(?:ь|и|ей)|сут(?:ки|ок)|день|дня|дней|недел[яиюь]|месяц(?:а|ев)?|год(?:а|ов)?|лет|(?:tun|kun|hafta|oy|yil)(?:ga)?|(?:тун|кун|ҳафта|ой|йил)(?:га)?|nights?|days?|weeks?|months?|years?)";
    private const string ExplicitCurrency = @"(?:usd|доллар(?:ов|а)?|uzs|сум(?:ов|а)?|сўм|so'm|eur|евро|rub|kzt|[$€₽₸])";

    internal static (RentalTermsAnswer? Answer, string? Reason) Parse(string? output, string description, int visibleLength)
    {
        if (string.IsNullOrWhiteSpace(output))
            return (null, "The model returned an empty answer.");

        RentalTermsAnswer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<RentalTermsAnswer>(output, RentalTermsDefinition.Wire);
        }
        catch (JsonException)
        {
            return (null, "The model returned an invalid rental-terms document.");
        }

        if (answer is null)
            return (null, "The model returned an invalid rental-terms document.");
        if (!Verify(answer.Deposit, description, visibleLength, DepositCue(), Deposit))
            return (null, "Deposit claims did not match the description.");
        if (!Verify(answer.MinimumStay, description, visibleLength, MinimumCue(), Duration))
            return (null, "Minimum-stay claims did not match the description.");
        if (!Verify(answer.AdvanceRent, description, visibleLength, AdvanceCue(), Advance))
            return (null, "Advance-rent claims did not match the description.");
        if (!Verify(answer.Utilities, description, visibleLength, UtilitiesCue(), Utilities, ambiguousExclusion: true))
            return (null, "Utilities claims did not match the description.");

        return (answer, null);
    }

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
            if (string.IsNullOrWhiteSpace(claim?.Evidence))
                return false;
            // The quote must fit in the model's input. Token and clause checks use the saved description.
            int offset = description.IndexOf(claim.Evidence, 0, visibleLength, StringComparison.Ordinal);
            if (offset < 0 || !HasWholeTokens(description, offset, claim.Evidence.Length))
                return false;
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

    private static bool Deposit(RentalDeposit value, string quote, Regex cue) =>
        Payment(value.Amount, value.Currency, value.RentMultiple, quote, cue);

    private static bool Advance(RentalAdvance value, string quote, Regex cue) => value.Duration is { } duration
        ? value.Amount is null && value.Currency is null && value.RentMultiple is null && Duration(duration, quote, cue)
        : Payment(value.Amount, value.Currency, value.RentMultiple, quote, cue);

    private static bool Payment(decimal? amount, string? currency, decimal? rentMultiple, string quote, Regex cue)
    {
        if (amount is null && currency is null && rentMultiple is null)
            return !Quantity().IsMatch(quote) && !CurrencyName().IsMatch(quote) && !DurationNumber().IsMatch(quote)
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
        var currencies = CurrencyName().Matches(quote);
        return numbers.Count == 1 && MatchesMoney(quote, cue, numbers[0], currencies, amount.Value, currency);
    }

    private static bool MatchesMoney(string quote, Regex cue, Match number, MatchCollection currencies, decimal expected, string? currency)
    {
        int end = number.Index + number.Length;
        var scale = MoneyScale().Match(quote[end..]);
        string token = scale.Groups["scale"].Value;
        decimal multiplier = token switch
        {
            "million" or "mln" or "миллион" or "миллиона" or "миллионов" or "млн" => 1_000_000m,
            "" => 1m,
            _ => 1_000m,
        };
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
        _ => "UZS",
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

    [GeneratedRegex(@"^\s*(?<scale>тыс(?:яч(?:а|и)?)?|млн|миллион(?:а|ов)?|ming|минг|million|mln|k)\b\.?", PatternOptions)]
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

    [GeneratedRegex(@"\b(?:коммунал[а-я]*|квартплат[а-я]*|kommunal\w*|utility|utilities)\b", PatternOptions)]
    private static partial Regex UtilitiesCue();

    [GeneratedRegex(@"\b(?<word>без|нет|no|without)\s+(?:отдельн[а-я]*\s+)?$", PatternOptions)]
    private static partial Regex AbsenceBefore();

    [GeneratedRegex(@"^\s*[:=]?\s*(?:(?:" + Number + @")\s*(?:" + ExplicitCurrency + "|" + TimeUnit + @")?\s+)?(?:не\s+(?:нуж[а-я]*|требу[а-я]*|берется|взимается|предусмотр[а-я]*)|нет|отсутств[а-я]*|отмен[а-я]*|yo'q|йўқ|olinmaydi|олинмайди|talab\s+qilinmaydi|талаб\s+қилинмайди|shart\s+emas|шарт\s+эмас|none|not\s+required|waived|0(?:[.,]0+)?)(?![\p{L}\d]|[.,]\d)", PatternOptions)]
    private static partial Regex AbsenceAfter();

    [GeneratedRegex(@"\b(?:не\s+включ[а-я]*|(?:совсем\s+)?отдельно|not\s+included|separately|alohida|алоҳида|kiritilmagan|киритилмаган)\b|\+\s*(?:коммунал[а-я]*|utilities|kommunal\w*)", PatternOptions)]
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
