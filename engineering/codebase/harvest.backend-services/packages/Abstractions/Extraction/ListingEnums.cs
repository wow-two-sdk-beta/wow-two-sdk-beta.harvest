namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Refers to the property kind.</summary>
public enum ListingPropertyKind
{
    /// <summary>Apartment.</summary>
    Apartment,
    /// <summary>House.</summary>
    House,
    /// <summary>Room.</summary>
    Room,
    /// <summary>Townhouse.</summary>
    Townhouse,
    /// <summary>Cottage.</summary>
    Cottage,
    /// <summary>Villa.</summary>
    Villa,
    /// <summary>Land.</summary>
    Land,
    /// <summary>Office.</summary>
    Office,
    /// <summary>Retail.</summary>
    Retail,
    /// <summary>Warehouse.</summary>
    Warehouse,
    /// <summary>Parking.</summary>
    Parking,
    /// <summary>Another stated value.</summary>
    Other,
}

/// <summary>Refers to the segment in which the property is offered.</summary>
public enum ListingMarketSegment
{
    /// <summary>New construction.</summary>
    NewBuild,
    /// <summary>Secondary market.</summary>
    Resale,
    /// <summary>Another stated value.</summary>
    Other,
}

/// <summary>Refers to a published construction label.</summary>
public enum ListingConstruction
{
    /// <summary>Brick.</summary>
    Brick,
    /// <summary>Panel.</summary>
    Panel,
    /// <summary>Monolith.</summary>
    Monolith,
    /// <summary>Block.</summary>
    Block,
    /// <summary>Wood.</summary>
    Wood,
    /// <summary>Another stated value.</summary>
    Other,
}

/// <summary>Refers to the finish stage.</summary>
public enum ListingFinishing
{
    /// <summary>Rough or shell finish.</summary>
    Shell,
    /// <summary>Preparatory finish.</summary>
    PreFinish,
    /// <summary>Finished.</summary>
    Finished,
    /// <summary>Another stated value.</summary>
    Other,
}

/// <summary>Refers to the publisher’s renovation descriptor.</summary>
public enum ListingRenovation
{
    /// <summary>Publisher-described European renovation.</summary>
    Euro,
    /// <summary>Designer.</summary>
    Designer,
    /// <summary>Average.</summary>
    Average,
    /// <summary>Needs renovation.</summary>
    NeedsRepair,
    /// <summary>Another stated value.</summary>
    Other,
}

/// <summary>Refers to what furnishing is offered with the unit.</summary>
public enum ListingFurnishing
{
    /// <summary>Unfurnished.</summary>
    Unfurnished,
    /// <summary>Partial.</summary>
    Partial,
    /// <summary>Furnished.</summary>
    Furnished,
    /// <summary>Another stated value.</summary>
    Other,
}

/// <summary>Refers to how the bathroom and toilet are arranged.</summary>
public enum ListingBathroomArrangement
{
    /// <summary>Separate.</summary>
    Separate,
    /// <summary>Combined.</summary>
    Combined,
    /// <summary>Another stated value.</summary>
    Other,
}

/// <summary>Refers to the unit’s layout style.</summary>
public enum ListingLayout
{
    /// <summary>Studio.</summary>
    Studio,
    /// <summary>Multi Level.</summary>
    MultiLevel,
    /// <summary>Penthouse.</summary>
    Penthouse,
    /// <summary>A compact family unit.</summary>
    CompactFamily,
    /// <summary>Another stated value.</summary>
    Other,
}

/// <summary>Refers to how the rooms connect.</summary>
public enum ListingRoomConnectivity
{
    /// <summary>Separate.</summary>
    Separate,
    /// <summary>Adjacent.</summary>
    Adjacent,
    /// <summary>Includes separate and adjacent rooms.</summary>
    Mixed,
    /// <summary>Another stated value.</summary>
    Other,
}

/// <summary>Refers to the offered transaction.</summary>
public enum ListingTransaction
{
    /// <summary>Rent.</summary>
    Rent,
    /// <summary>Sale.</summary>
    Sale,
    /// <summary>Exchange.</summary>
    Exchange,
    /// <summary>Another stated value.</summary>
    Other,
}

/// <summary>Refers to the unit of a stated rent period.</summary>
public enum ListingPeriod
{
    /// <summary>Hour.</summary>
    Hour,
    /// <summary>Night.</summary>
    Night,
    /// <summary>Day.</summary>
    Day,
    /// <summary>Week.</summary>
    Week,
    /// <summary>Month.</summary>
    Month,
    /// <summary>Year.</summary>
    Year,
}

/// <summary>Refers to the role of a published construction year.</summary>
public enum ListingBuildYearRole
{
    /// <summary>Built.</summary>
    Built,
    /// <summary>Handover.</summary>
    Handover,
    /// <summary>A year that may describe construction or handover.</summary>
    BuiltOrHandover,
}

/// <summary>Refers to where a listing feature is found.</summary>
public enum ListingFeatureScope
{
    /// <summary>Unit.</summary>
    Unit,
    /// <summary>Building.</summary>
    Building,
    /// <summary>Complex.</summary>
    Complex,
    /// <summary>Nearby.</summary>
    Nearby,
}

/// <summary>Refers to the marketplace account class.</summary>
public enum ListingAccountClass
{
    /// <summary>Business.</summary>
    Business,
    /// <summary>A marketplace private account.</summary>
    PrivateIndividual,
    /// <summary>Another stated value.</summary>
    Other,
}
