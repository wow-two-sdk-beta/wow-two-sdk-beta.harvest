namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

/// <summary>The reviewed marketplace profiles setup can apply.</summary>
public static class MarketplaceProfiles
{
    /// <summary>Holds the order every OLX search uses: newest ads first, so a bounded run reads what changed since the last.</summary>
    private const string NewestFirst = "?search%5Border%5D=created_at%3Adesc";

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
                "https://www.olx.uz/nedvizhimost/kvartiry/arenda-dolgosrochnaya/tashkent/" + NewestFirst),
            new("houses-rent", "OLX · Tashkent houses, long-term rent · Haven",
                "https://www.olx.uz/nedvizhimost/doma/arenda-dolgosrochnaya/tashkent/" + NewestFirst),
            new("daily-rent", "OLX · Tashkent daily rent · Haven",
                "https://www.olx.uz/nedvizhimost/posutochno_pochasovo/tashkent/" + NewestFirst),
        ],
    };

    /// <summary>Gets every profile, in key order.</summary>
    public static IReadOnlyList<MarketplaceProfile> All { get; } = [Haven];

    /// <summary>Finds a profile by its key, or null.</summary>
    /// <param name="key">The profile's key.</param>
    /// <returns>The profile, or null when none has that key.</returns>
    public static MarketplaceProfile? Find(string key) => All.FirstOrDefault(profile => profile.Key == key);
}
