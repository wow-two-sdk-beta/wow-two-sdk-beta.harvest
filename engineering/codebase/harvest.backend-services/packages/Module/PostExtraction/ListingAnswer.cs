using System.Text.Json.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Represents an <c>extract-listing@2</c> answer as the schema states it: the facts of one listing, a real-estate ad
/// first, widened from Haven's classification fields minus every contact (no name, phone, registration date or confidence).</summary>
/// <remarks>
///   - every field is required: an unstated fact is <c>null</c>, <c>unknown</c>, <c>false</c> or <c>[]</c> as the field says, and
///     every pick but <c>invalid</c> and <c>relation.status</c> allows <c>unsure</c>, which the check flags (scfd-entity-model #9)
///   - a nullable pick is <c>null</c> when the post says nothing and <c>unsure</c> when it says something the model cannot read
///   - <c>item</c> keeps today's path for the unit and building facts the entity layer reads (<c>rooms</c>, <c>floor</c>,
///     <c>floors</c>, <c>building</c>, <c>areaM2</c> now <c>{total, living, kitchen}</c>); <c>brand</c> and <c>model</c> are gone
///   - the price, each rental-term claim and each landmark carry a verbatim quote the check keeps or drops
///   - <c>rentalTerms</c> stays <c>null</c> but for a rent offer under <c>real-estate</c>; a term's <c>status</c> reads
///     <c>absent</c> for a stated absence ("no commission"), <c>unknown</c> for silence
/// </remarks>
/// <param name="Title">A short title in the post's own words.</param>
/// <param name="CategoryPath">The source's own category path.</param>
/// <param name="PropertyKind">What kind of property the listing offers, or <c>null</c> for a post that is no offer.</param>
/// <param name="Invalid">Why the post is no usable offer, or <c>null</c> when it is one.</param>
/// <param name="OfferType">What the listing offers.</param>
/// <param name="Price">The asking price, or <c>null</c> when the post states no amount.</param>
/// <param name="Item">The offered thing: the building and the unit.</param>
/// <param name="Furnished">Whether the unit is furnished.</param>
/// <param name="ApartmentAmenities">What the unit has.</param>
/// <param name="NearbyAmenities">What is near the building.</param>
/// <param name="Location">Where the offer is.</param>
/// <param name="Landmarks">Named places the post points to.</param>
/// <param name="Author">Who offers it, by kind only.</param>
/// <param name="NoAgents">Whether the post refuses agents; <c>false</c> when it says nothing.</param>
/// <param name="Relation">The post-to-entity relation.</param>
/// <param name="RentalTerms">The rental terms with their quotes, or <c>null</c> outside a real-estate rent.</param>
/// <param name="AcceptedTenants">The tenants the post welcomes.</param>
/// <param name="ExcludedTenants">The tenants the post refuses.</param>
/// <param name="Urgent">Whether the post calls itself urgent; <c>false</c> when it says nothing.</param>
/// <param name="Flags">The model's own flags and the check's.</param>
internal sealed record ExtractListingAnswer(
    string Title,
    IReadOnlyList<string> CategoryPath,
    ListingKind? PropertyKind,
    ListingInvalidReason? Invalid,
    ListingOfferType OfferType,
    ListingAsk? Price,
    ListingItem Item,
    ListingFurnished Furnished,
    IReadOnlyList<ListingApartmentAmenity> ApartmentAmenities,
    IReadOnlyList<ListingNearbyAmenity> NearbyAmenities,
    ListingLocation Location,
    IReadOnlyList<ListingLandmark> Landmarks,
    ListingAuthor Author,
    bool NoAgents,
    EntityRelation Relation,
    ListingRentalTerms? RentalTerms,
    IReadOnlyList<ListingTenant> AcceptedTenants,
    IReadOnlyList<ListingTenant> ExcludedTenants,
    bool Urgent,
    IReadOnlyList<PostTagsFlag> Flags);

/// <summary>Refers to the kind of property a listing offers.</summary>
internal enum ListingKind
{
    Apartment,
    House,
    Room,
    [JsonStringEnumMemberName("bed-space")] BedSpace,
    Commercial,
    Land,
    Unsure,
}

/// <summary>Refers to why a post is no usable offer; a usable one has none.</summary>
internal enum ListingInvalidReason
{
    [JsonStringEnumMemberName("not-a-property")] NotAProperty,
    [JsonStringEnumMemberName("multi-listing")] MultiListing,
}

/// <summary>Refers to what the listing offers.</summary>
internal enum ListingOfferType
{
    Rent,
    Sale,
    [JsonStringEnumMemberName("rent-or-sale")] RentOrSale,
    Exchange,
    [JsonStringEnumMemberName("rent-to-own")] RentToOwn,
    Unsure,
}

/// <summary>Represents the asking price as written, its period for a rent, whether it is negotiable, and the quote it comes from.</summary>
internal sealed record ListingAsk(decimal Amount, ListingCurrency Currency, ListingAskPeriod? Period, bool Negotiable, string Evidence);

/// <summary>Refers to the currency a price or a payment is written in, as written.</summary>
internal enum ListingCurrency { Usd, Uzs, Unsure }

/// <summary>Refers to the period a rent price covers.</summary>
internal enum ListingAskPeriod { Hour, Night, Day, Week, Month, Year, Unsure }

/// <summary>Represents the offered thing: the building (market, material, year, name) and the unit (rooms, layout, areas, floors,
/// ceiling, bathrooms, renovation).</summary>
internal sealed record ListingItem(
    ListingMarket? PropertyType,
    ListingBuildingType? BuildingType,
    int? YearBuilt,
    int? Rooms,
    ListingRoomLayout? Layout,
    ListingAreas AreaM2,
    int? Floor,
    int? Floors,
    decimal? CeilingM,
    ListingBathrooms Bathrooms,
    ListingRenovationLevel? Renovation,
    string? Building);

/// <summary>Represents the unit's areas in square metres.</summary>
internal sealed record ListingAreas(decimal? Total, decimal? Living, decimal? Kitchen);

/// <summary>Represents the unit's bathrooms: how many and how they are arranged.</summary>
internal sealed record ListingBathrooms(int? Count, ListingBathroomType? Type);

/// <summary>Refers to the market a property is on.</summary>
internal enum ListingMarket
{
    [JsonStringEnumMemberName("new-build")] NewBuild,
    Secondary,
    Unsure,
}

/// <summary>Refers to what a building is made of.</summary>
internal enum ListingBuildingType { Brick, Panel, Monolithic, Block, Wooden, Unsure }

/// <summary>Refers to how a unit's bathrooms are arranged.</summary>
internal enum ListingBathroomType { Separate, Combined, Mixed, Unsure }

/// <summary>Refers to how a unit's rooms are laid out.</summary>
internal enum ListingRoomLayout
{
    Separate,
    [JsonStringEnumMemberName("adjacent-separate")] AdjacentSeparate,
    Adjacent,
    Studio,
    [JsonStringEnumMemberName("multi-level")] MultiLevel,
    Penthouse,
    [JsonStringEnumMemberName("small-family")] SmallFamily,
    Unsure,
}

/// <summary>Refers to a unit's renovation level.</summary>
internal enum ListingRenovationLevel
{
    Shell,
    [JsonStringEnumMemberName("rough-finish")] RoughFinish,
    [JsonStringEnumMemberName("needs-renovation")] NeedsRenovation,
    [JsonStringEnumMemberName("with-renovation")] WithRenovation,
    Euro,
    Designer,
    Unsure,
}

/// <summary>Refers to whether a unit comes furnished.</summary>
internal enum ListingFurnished { Yes, Partly, No, Unknown, Unsure }

/// <summary>Refers to what a unit has.</summary>
internal enum ListingApartmentAmenity
{
    Internet,
    Phone,
    Refrigerator,
    Tv,
    [JsonStringEnumMemberName("air-conditioner")] AirConditioner,
    [JsonStringEnumMemberName("cable-tv")] CableTv,
    [JsonStringEnumMemberName("washing-machine")] WashingMachine,
    Kitchen,
    Balcony,
    Dishwasher,
    [JsonStringEnumMemberName("smart-home")] SmartHome,
    [JsonStringEnumMemberName("water-heater")] WaterHeater,
    Oven,
    Microwave,
    Iron,
    [JsonStringEnumMemberName("vacuum-cleaner")] VacuumCleaner,
    Wardrobe,
    [JsonStringEnumMemberName("bed-linen")] BedLinen,
    Elevator,
    [JsonStringEnumMemberName("parking-spot")] ParkingSpot,
    Security,
    Generator,
    Gas,
    [JsonStringEnumMemberName("central-heating")] CentralHeating,
    Unsure,
}

/// <summary>Refers to what is near a building.</summary>
internal enum ListingNearbyAmenity
{
    Hospital,
    School,
    Playground,
    Kindergarten,
    [JsonStringEnumMemberName("bus-stops")] BusStops,
    Park,
    Entertainment,
    Restaurants,
    Parking,
    Supermarket,
    University,
    Mosque,
    Metro,
    Market,
    Unsure,
}

/// <summary>Represents where the offer is.</summary>
internal sealed record ListingLocation(string? District, string? City, string? Address);

/// <summary>Represents one named place the post points to: its type, its name as written and the quote it comes from.</summary>
internal sealed record ListingLandmark(ListingLandmarkType Type, string Name, string Evidence);

/// <summary>Refers to a kind of place, a subset of Haven's landmark types plus <c>street</c>.</summary>
internal enum ListingLandmarkType
{
    [JsonStringEnumMemberName("residential-complex")] ResidentialComplex,
    Massiv,
    Street,
    [JsonStringEnumMemberName("metro-station")] MetroStation,
    Bazaar,
    Supermarket,
    [JsonStringEnumMemberName("shopping-mall")] ShoppingMall,
    School,
    Kindergarten,
    University,
    Hospital,
    Park,
    Mosque,
    Intersection,
    Other,
    Unsure,
}

/// <summary>Represents who offers it, by kind only: no name, phone, contact or registration date is part of the answer.</summary>
internal sealed record ListingAuthor(ListingAuthorKind Kind);

/// <summary>Refers to who offers the listing.</summary>
internal enum ListingAuthorKind
{
    Owner,
    Agent,
    Agency,
    Developer,
    [JsonStringEnumMemberName("management-company")] ManagementCompany,
    Unknown,
    Unsure,
}

/// <summary>Refers to a kind of tenant a post welcomes or refuses.</summary>
internal enum ListingTenant
{
    Family,
    Expats,
    Girls,
    Boys,
    Women,
    Men,
    Students,
    Singles,
    Workers,
    Military,
    Responsible,
    Children,
    Animals,
    Smoking,
    Drinking,
    Unsure,
}

/// <summary>Refers to the unit of a stay or a prepaid period.</summary>
internal enum ListingDurationUnit { Hour, Night, Day, Week, Month, Year, Unsure }

/// <summary>Refers to how utilities are billed.</summary>
internal enum ListingUtilitiesBilling { Included, Separate, Partial, Unsure }

/// <summary>Represents a duration such as a minimum stay.</summary>
internal sealed record ListingDuration(decimal Count, ListingDurationUnit Unit);

/// <summary>Represents a deposit: an amount in a currency, or a multiple of the rent.</summary>
internal sealed record ListingDeposit(decimal? Amount, ListingCurrency? Currency, decimal? RentMultiple);

/// <summary>Represents advance rent: an amount, a multiple of the rent, or a duration.</summary>
internal sealed record ListingAdvance(decimal? Amount, ListingCurrency? Currency, decimal? RentMultiple, ListingDuration? Duration);

/// <summary>Represents how utilities are billed.</summary>
internal sealed record ListingUtilities(ListingUtilitiesBilling Billing);

/// <summary>Represents a commission: an amount, a percent or a multiple of the rent; none of them for a commission with no
/// stated size.</summary>
internal sealed record ListingCommission(decimal? Amount, ListingCurrency? Currency, decimal? Percent, decimal? RentMultiple);

/// <summary>Represents the five rental terms of <c>extract-listing@2</c>, each with its evidence: the four of
/// <c>rental-terms@2</c> with lowercase currency and unit enums, and the commission.</summary>
internal sealed record ListingRentalTerms(
    RentalTerm<ListingDeposit> Deposit,
    RentalTerm<ListingAdvance> AdvanceRent,
    RentalTerm<ListingUtilities> Utilities,
    RentalTerm<ListingDuration> MinimumStay,
    RentalTerm<ListingCommission> Commission)
{
    /// <summary>Gets the terms with every one unknown.</summary>
    internal static ListingRentalTerms Empty { get; } = new(RentalTerm<ListingDeposit>.Unknown, RentalTerm<ListingAdvance>.Unknown,
        RentalTerm<ListingUtilities>.Unknown, RentalTerm<ListingDuration>.Unknown, RentalTerm<ListingCommission>.Unknown);
}
