using WoW.Two.Sdk.Beta.Be.HarvestAdapters.BirBir;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Joymee;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Olx;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

/// <summary>The reviewed marketplace profiles setup can apply.</summary>
public static class MarketplaceProfiles
{
    /// <summary>Haven's Tashkent residential rent (<c>rent-res</c>): long-term apartments and houses, and daily rent.</summary>
    /// <remarks>Category slugs as OLX's own category tree names them (read 2026-11-14). Haven v1.0 read every Tashkent
    /// apartment and ListingShelf the same category; rent-only categories leave sales out before classification.</remarks>
    public static MarketplaceProfile Haven { get; } = new()
    {
        Key = "haven",
        TenantSlug = "haven",
        TenantName = "Haven listings",
        ContainerRef = "haven-listings",
        Consumer = "haven-supply",
        Catalog = "haven-rent-res",
        Searches =
        [
            new("apartments-rent", "OLX · Tashkent apartments, long-term rent · Haven",
                OlxSourceAdapter.ApartmentRentCatalog, MarketplaceSite.Olx),
            new("houses-rent", "OLX · Tashkent houses, long-term rent · Haven",
                OlxSourceAdapter.HouseRentCatalog, MarketplaceSite.Olx),
            new("daily-rent", "OLX · Tashkent daily rent · Haven",
                OlxSourceAdapter.DailyRentCatalog, MarketplaceSite.Olx),
            new("joymee-apartments-rent", "Joymee · Tashkent apartments, long-term rent · Haven",
                JoymeeSourceAdapter.RentCatalog, MarketplaceSite.Joymee),
            new("birbir-apartments-rent", "BirBir · Tashkent apartment imports · Haven",
                BirBirSourceAdapter.RentalCatalog, MarketplaceSite.BirBir),
            new("birbir-collection", "BirBir · Tashkent apartment rentals · Haven",
                BirBirSourceAdapter.RentalCatalog, MarketplaceSite.BirBirCollection),
            new("domtut-apartments-rent", "Domtut · Tashkent rental imports · Haven",
                "https://domtut.uz/filter/search?format=rent", MarketplaceSite.Domtut),
        ],
    };

    /// <summary>Gets every profile, in key order.</summary>
    public static IReadOnlyList<MarketplaceProfile> All { get; } = [Haven];

    /// <summary>Finds a profile by its key, or null.</summary>
    /// <param name="key">The profile's key.</param>
    /// <returns>The profile, or null when none has that key.</returns>
    public static MarketplaceProfile? Find(string key) => All.FirstOrDefault(profile => profile.Key == key);
}
