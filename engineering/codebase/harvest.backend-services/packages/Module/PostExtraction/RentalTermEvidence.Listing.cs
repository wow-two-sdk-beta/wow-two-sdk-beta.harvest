using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>The evidence check of <c>extract-listing@2</c>'s five rental terms: the four of <c>rental-terms@2</c> read through
/// the same per-term rules (<c>RentalTermEvidence.cs</c>, unchanged) and the commission.</summary>
/// <remarks>
///   - the terms carry lowercase currency and unit enums; each claim is mapped to the version-2 value it states, and a value the
///     version-2 rules cannot read (an <c>unsure</c> currency next to a quote that names one, an hour or <c>unsure</c> unit, an
///     <c>unsure</c> billing) turns its term unknown, with a flag, like any quote that fails
///   - the text a quote may come from is the caller's: the title, the text and the attribute lines, each its own source
///   - a commission is an amount in a currency, a percent or a multiple of the rent; <c>absent</c> needs "no commission", "0%" or
///     a cue followed by "no"; a present commission with no stated size needs a word that says it is charged or required
/// </remarks>
internal static partial class RentalTermEvidence
{
    /// <summary>Checks each term of <paramref name="answer"/> on its own: a term whose claims fail becomes unknown, and its field
    /// name (<c>deposit</c>, <c>minimumStay</c>, <c>advanceRent</c>, <c>utilities</c>, <c>commission</c>) is listed as rejected.</summary>
    /// <param name="answer">The model's terms.</param>
    /// <param name="description">The text the quotes must come from.</param>
    /// <param name="visibleLength">How much of <paramref name="description"/> the model read.</param>
    internal static (ListingRentalTerms Answer, IReadOnlyList<string> Rejected) CheckListingTerms(ListingRentalTerms answer, string description,
        int visibleLength)
    {
        var rejected = new List<string>();
        var deposit = Keep(answer.Deposit, VerifyDeposit(MapTerm(answer.Deposit, ToDeposit), description, visibleLength, conventionalUnits: true),
            "deposit", rejected);
        var minimum = Keep(answer.MinimumStay, VerifyMinimumStay(MapTerm(answer.MinimumStay, ToDuration), description, visibleLength),
            "minimumStay", rejected);
        var advance = Keep(answer.AdvanceRent, VerifyAdvanceRent(MapTerm(answer.AdvanceRent, ToAdvance), description, visibleLength, conventionalUnits: true),
            "advanceRent", rejected);
        var utilities = Keep(answer.Utilities, VerifyUtilities(MapTerm(answer.Utilities, ToUtilities), description, visibleLength),
            "utilities", rejected);
        var commission = Keep(answer.Commission, VerifyCommission(answer.Commission, description, visibleLength), "commission", rejected);
        return (new ListingRentalTerms(deposit, advance, utilities, minimum, commission), rejected);
    }

    private static bool VerifyCommission(RentalTerm<ListingCommission>? term, string description, int visibleLength) =>
        Verify(term, description, visibleLength, CommissionCue(), Commission);

    private static bool Commission(ListingCommission value, string quote, Regex cue)
    {
        if (value.Percent is { } percent)
        {
            if (value.Amount is not null || value.Currency is not null || value.RentMultiple is not null || percent is <= 0 or > 100)
                return false;
            var found = PercentValue().Matches(quote);
            return found.Count == 1 && CountOf(found[0].Groups["count"].Value) == percent && Connected(quote, cue, found[0]);
        }
        if (PercentValue().IsMatch(quote))
            return false;
        if (value is { Amount: null, Currency: null, RentMultiple: null })
            return !Quantity().IsMatch(quote) && (CommissionCharged().IsMatch(quote) || RequiredPayment().IsMatch(quote));
        return Payment(value.Amount, CurrencyName(value.Currency), value.RentMultiple, quote, cue, conventionalUnits: true);
    }

    /// <summary>Maps a term's claims to the shared version-2 value types; null when one claim holds a value they cannot read.</summary>
    private static RentalTerm<TTo>? MapTerm<TFrom, TTo>(RentalTerm<TFrom>? term, Func<TFrom, TTo?> map)
        where TFrom : class
        where TTo : class
    {
        if (term?.Claims is null)
            return null;
        var claims = new List<RentalClaim<TTo>>();
        foreach (var claim in term.Claims)
        {
            if (claim is null)
                return null;
            TTo? value = null;
            if (claim.Value is not null && (value = map(claim.Value)) is null)
                return null;
            claims.Add(new RentalClaim<TTo>(claim.Evidence, value));
        }
        return new RentalTerm<TTo>(term.Status, claims);
    }

    private static RentalDeposit ToDeposit(ListingDeposit value) => new(value.Amount, CurrencyName(value.Currency), value.RentMultiple);

    private static RentalAdvance? ToAdvance(ListingAdvance value)
    {
        if (value.Duration is null)
            return new RentalAdvance(value.Amount, CurrencyName(value.Currency), value.RentMultiple, null);
        return ToDuration(value.Duration) is { } duration
            ? new RentalAdvance(value.Amount, CurrencyName(value.Currency), value.RentMultiple, duration) : null;
    }

    private static RentalDuration? ToDuration(ListingDuration value) => value.Unit switch
    {
        ListingDurationUnit.Night => new RentalDuration(value.Count, RentalDurationUnit.Night),
        ListingDurationUnit.Day => new RentalDuration(value.Count, RentalDurationUnit.Day),
        ListingDurationUnit.Week => new RentalDuration(value.Count, RentalDurationUnit.Week),
        ListingDurationUnit.Month => new RentalDuration(value.Count, RentalDurationUnit.Month),
        ListingDurationUnit.Year => new RentalDuration(value.Count, RentalDurationUnit.Year),
        _ => null,
    };

    private static RentalUtilities? ToUtilities(ListingUtilities value) => value.Billing switch
    {
        ListingUtilitiesBilling.Included => new RentalUtilities(RentalUtilitiesBilling.Included),
        ListingUtilitiesBilling.Separate => new RentalUtilities(RentalUtilitiesBilling.Separate),
        ListingUtilitiesBilling.Partial => new RentalUtilities(RentalUtilitiesBilling.Partial),
        _ => null,
    };

    /// <summary>Names a currency enum as the version-2 check reads it; <c>unsure</c> and none read as no currency, which only a
    /// quote that names none can back.</summary>
    private static string? CurrencyName(ListingCurrency? currency) => currency switch
    {
        ListingCurrency.Usd => "USD",
        ListingCurrency.Uzs => "UZS",
        _ => null,
    };

    [GeneratedRegex(@"\b(?:комм?исс?[а-я]*|komm?iss?[a-z']*|commission|vositachilik\s+haqq?i|воситачилик\s+ҳақи"
        + @"|(?:ри[эе]лторск|маклерск|агентск)[а-я]*\s+услуг[а-я]*|услуг[а-я]*\s+(?:ри[эе]лтор|маклер|агент)[а-я]*"
        + @"|(?:broker|agent)\s+fee|makler(?:lik)?\s+(?:xizmati|haqq?i))\b", PatternOptions)]
    private static partial Regex CommissionCue();

    [GeneratedRegex(@"(?<![\p{L}\d])(?<count>" + Number + @")\s*(?:%|процент(?:ов|а)?(?![\p{L}])|проц(?![\p{L}])|foiz\w*|фоиз\w*|percent(?![\p{L}]))", PatternOptions)]
    private static partial Regex PercentValue();

    [GeneratedRegex(@"\b(?:есть|да|имеется|присутствует|взимается|берется|yes|bor|mavjud|olinadi|бор|мавжуд|олинади)\b", PatternOptions)]
    private static partial Regex CommissionCharged();
}
