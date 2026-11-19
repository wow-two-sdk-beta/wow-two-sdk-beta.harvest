namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Represents a price an offer asks: its amount, currency and, for a rent, its period.</summary>
internal sealed record ListingPrice(decimal Amount, string? Currency, string? Period);

/// <summary>Represents what identifies an offered thing across ads: the offer, the source's category path, the place and
/// the item's own facts, with the prices its offers ask.</summary>
internal sealed record ListingFacts
{
    internal string? OfferType { get; init; }
    internal IReadOnlyList<string> CategoryPath { get; init; } = [];
    internal string? City { get; init; }
    internal string? District { get; init; }
    internal int? Rooms { get; init; }
    internal decimal? AreaM2 { get; init; }
    internal int? Floor { get; init; }
    internal int? Floors { get; init; }
    internal string? Building { get; init; }
    internal string? Brand { get; init; }
    internal string? Model { get; init; }
    internal int? Year { get; init; }
    internal IReadOnlyList<ListingPrice> Prices { get; init; } = [];
}

/// <summary>Scores whether an ad offers the same thing as a listing, 0 to 1: the item-identity match of the build plan.</summary>
/// <remarks>
///   - a known fact that differs rules the pair out: offer type, category path (neither a prefix of the other), city,
///     district, rooms, area beyond the tolerance (5% by default), floor, floor count, building, brand, model or year
///   - each fact both sides know and share adds its weight: area 0.3, district 0.2, rooms 0.2, model 0.2, price band 0.15,
///     building 0.15, floor 0.1, brand 0.1, year 0.1, city 0.05, category path 0.05, floor count 0.05
///   - a price outside the band (20% by default) beyond every offer of the listing, in the same currency and period,
///     takes 0.2 off; the same author adds 0.2
/// </remarks>
internal static class ListingIdentity
{
    /// <summary>Scores an ad against a listing.</summary>
    /// <param name="ad">The ad's facts.</param>
    /// <param name="listing">The listing's merged facts and its offers' prices.</param>
    /// <param name="sameAuthor">Whether an offer of the listing has the ad's author on the ad's source.</param>
    /// <param name="options">The area tolerance and price band.</param>
    internal static double Score(ListingFacts ad, ListingFacts listing, bool sameAuthor, EntityOptions options)
    {
        if (!Agree(ad.OfferType, listing.OfferType) || !Path(ad.CategoryPath, listing.CategoryPath))
            return 0;
        double score = 0;
        if (Both(ad.City, listing.City))
        {
            if (!EntityNames.SamePlace(EntityNames.PlaceKey(ad.City), EntityNames.PlaceKey(listing.City)))
                return 0;
            score += 0.05;
        }
        if (Both(ad.District, listing.District))
        {
            if (!EntityNames.SamePlace(EntityNames.PlaceKey(ad.District), EntityNames.PlaceKey(listing.District)))
                return 0;
            score += 0.2;
        }
        if (ad.AreaM2 is { } area && listing.AreaM2 is { } known)
        {
            if (Math.Abs(area - known) > known * (decimal)options.ListingAreaTolerance)
                return 0;
            score += 0.3;
        }
        foreach (var (left, right, weight) in new (object? Left, object? Right, double Weight)[]
                 {
                     (ad.Rooms, listing.Rooms, 0.2), (ad.Floor, listing.Floor, 0.1), (ad.Floors, listing.Floors, 0.05),
                     (ad.Year, listing.Year, 0.1),
                 })
        {
            if (left is null || right is null)
                continue;
            if (!left.Equals(right))
                return 0;
            score += weight;
        }
        foreach (var (left, right, weight) in new (string? Left, string? Right, double Weight)[]
                 {
                     (ad.Building, listing.Building, 0.15), (ad.Brand, listing.Brand, 0.1), (ad.Model, listing.Model, 0.2),
                 })
        {
            if (!Both(left, right))
                continue;
            if (EntityNames.Key(left) != EntityNames.Key(right))
                return 0;
            score += weight;
        }
        if (ad.CategoryPath.Count > 0 && listing.CategoryPath.Count > 0)
            score += 0.05;
        score += Band(ad.Prices, listing.Prices, options.ListingPriceBand);
        if (sameAuthor)
            score += 0.2;
        return Math.Clamp(score, 0, 1);
    }

    /// <summary>Adds 0.15 when the ad's price sits within the band of the listing's offers in the same currency and
    /// period, takes 0.2 off when it sits outside, and adds nothing when they share no currency and period.</summary>
    private static double Band(IReadOnlyList<ListingPrice> ad, IReadOnlyList<ListingPrice> listing, double band)
    {
        foreach (var price in ad)
        {
            var same = listing.Where(offer => Same(offer.Currency, price.Currency) && Same(offer.Period, price.Period)).ToArray();
            if (same.Length == 0 || price.Amount <= 0)
                continue;
            decimal low = same.Min(offer => offer.Amount) * (decimal)(1 - band);
            decimal high = same.Max(offer => offer.Amount) * (decimal)(1 + band);
            return price.Amount >= low && price.Amount <= high ? 0.15 : -0.2;
        }
        return 0;
    }

    private static bool Agree(string? left, string? right) => left is null || right is null || left == right;

    private static bool Both(string? left, string? right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right);

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether two category paths agree: either is unknown, or one is a prefix of the other, step by step.</summary>
    private static bool Path(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        int shared = Math.Min(left.Count, right.Count);
        for (int index = 0; index < shared; index++)
            if (EntityNames.Key(left[index]) != EntityNames.Key(right[index]))
                return false;
        return true;
    }
}
