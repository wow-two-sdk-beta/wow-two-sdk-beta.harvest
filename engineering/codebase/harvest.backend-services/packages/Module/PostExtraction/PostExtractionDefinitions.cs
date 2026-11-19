using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Describes the versioned <c>extract-event</c> and <c>extract-listing</c> use cases: their code-owned answer
/// schemas and the prompt variables the pass supplies.</summary>
/// <remarks>
///   - <c>extract-event</c>: <c>name</c> (<c>{en, original}</c>, without year or ordinal), <c>editionLabel</c>,
///     <c>occurrences</c> (<c>[{starts, ends, precision, certainty, evidence}]</c>), <c>place</c>, <c>organizer</c>,
///     <c>format</c>, <c>access</c>, <c>prices</c>, <c>registration</c>, <c>participation</c>, <c>status</c>,
///     <c>relation</c>, <c>fashion</c> and <c>flags</c>
///   - <c>extract-listing</c>: <c>title</c>, <c>offerType</c>, <c>categoryPath</c>, <c>price</c>, <c>location</c>,
///     <c>item</c>, <c>author</c> (<c>{kind}</c>, no names or contacts), <c>relation</c>, <c>rentalTerms</c> (the
///     <c>rental-terms@2</c> terms) and <c>flags</c>
///   - every date, price and rental term carries a verbatim <c>evidence</c> quote
/// </remarks>
public static class PostExtractionDefinitions
{
    /// <summary>The user and system template variables the pass supplies to both use cases, by name.</summary>
    public static IReadOnlyList<string> Variables { get; } =
    [
        "title", "text", "sourceCategory", "language", "dates", "place", "prices", "postedAt",
        "entityType", "domains", "about", "timeframe", "titleEn",
    ];

    /// <summary>Gets the JSON schema every <c>extract-event</c> answer must match, as the use case registry stores it.</summary>
    public static string EventSchema { get; } = BuildSchema(typeof(ExtractEventAnswer));

    /// <summary>Gets the JSON schema every <c>extract-listing</c> answer must match, as the use case registry stores it.</summary>
    public static string ListingSchema { get; } = BuildSchema(typeof(ExtractListingAnswer));

    /// <summary>Returns the schema of a use case, or null for another key.</summary>
    /// <param name="useCase"><c>extract-event</c> or <c>extract-listing</c>.</param>
    public static string? OutputSchema(string useCase) => useCase switch
    {
        PostExtractionConstants.EventUseCase => EventSchema,
        PostExtractionConstants.ListingUseCase => ListingSchema,
        _ => null,
    };

    /// <summary>Reads a model answer, or returns null when it does not match the schema's shape.</summary>
    internal static T? Parse<T>(string? output) where T : class
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(output, EvidenceWire.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildSchema(Type answer)
    {
        var options = new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true };
        return EvidenceWire.Options.GetJsonSchemaAsNode(answer, options).AsObject().ToJsonString();
    }
}

/// <summary>Represents an <c>extract-event</c> answer as the schema states it.</summary>
internal sealed record ExtractEventAnswer(
    EventName Name,
    string? EditionLabel,
    IReadOnlyList<EventOccurrence> Occurrences,
    EventPlace Place,
    string? Organizer,
    EventFormat Format,
    EventAccess Access,
    IReadOnlyList<EventPrice> Prices,
    EventRegistration Registration,
    IReadOnlyList<EventParticipation> Participation,
    EventStatus Status,
    EntityRelation Relation,
    FashionDetails? Fashion,
    IReadOnlyList<PostTagsFlag> Flags);

/// <summary>Represents the event's canonical name without year or ordinal, in English and as the source writes it.</summary>
internal sealed record EventName(string En, string? Original);

/// <summary>Represents one run of the event: ISO dates at a precision, whether the post states or implies them, and the
/// quote they come from.</summary>
internal sealed record EventOccurrence(string Starts, string? Ends, DatePrecision Precision, DateCertainty Certainty, string Evidence);

/// <summary>Represents where the event takes place.</summary>
internal sealed record EventPlace(string? Venue, string? City, string? Country);

/// <summary>Refers to what kind of event it is.</summary>
internal enum EventFormat
{
    Expo,
    [JsonStringEnumMemberName("trade-fair")] TradeFair,
    Runway,
    Festival,
    Competition,
    Conference,
    Market,
    Exhibition,
    Workshop,
    Concert,
    Other,
    Unsure,
}

/// <summary>Refers to how one gets in.</summary>
internal enum EventAccess { Free, Ticketed, Application, Unknown }

/// <summary>Represents a stated price: an amount (none for a price stated without one), its currency, what it buys and the
/// quote it comes from.</summary>
internal sealed record EventPrice(decimal? Amount, string? Currency, string? Label, string Evidence);

/// <summary>Represents registration: when it opens, its deadline and where to apply.</summary>
internal sealed record EventRegistration(EvidencedDate? Opens, EvidencedDate? Deadline, string? Link);

/// <summary>Represents one ISO date at a precision with the quote it comes from.</summary>
internal sealed record EvidencedDate(string Value, DatePrecision Precision, string Evidence);

/// <summary>Refers to who can take part.</summary>
internal enum EventParticipation { Exhibitor, Visitor, Contestant, Speaker, Unsure }

/// <summary>Refers to the event's state as the post tells it.</summary>
internal enum EventStatus { Scheduled, Cancelled, Postponed, Held, Unsure }

/// <summary>Represents the post-to-entity relation (scfd-entity-model #3.1): its role and, for an update, what changed.</summary>
internal sealed record EntityRelation(RelationRole Role, IReadOnlyList<RelationChange> Changes, RelationStatus? Status);

/// <summary>Refers to what the post does for its entity.</summary>
internal enum RelationRole { Announcement, Update, Report, Mention, Unsure }

/// <summary>Refers to one fact an update changes; <c>status</c> carries its new value in <see cref="EntityRelation.Status"/>.</summary>
internal enum RelationChange { Dates, Place, Price, Program, Registration, Deadline, Results, Status, Unsure }

/// <summary>Refers to the new status an update announces.</summary>
internal enum RelationStatus
{
    Cancelled,
    Postponed,
    [JsonStringEnumMemberName("sold-out")] SoldOut,
    Withdrawn,
}

/// <summary>Represents the fashion block, filled only when the post's primary domain is under <c>style-fashion</c>.</summary>
internal sealed record FashionDetails(
    IReadOnlyList<FashionSegment> Segments,
    IReadOnlyList<string> GarmentCategories,
    IReadOnlyList<FashionExhibitor> ExhibitorProfile,
    FashionAudience Audience);

/// <summary>Refers to a fashion segment the event covers.</summary>
internal enum FashionSegment { Apparel, Textile, Accessories, Jewelry, Footwear, Handmade, Beauty, Unsure }

/// <summary>Refers to who exhibits or shows.</summary>
internal enum FashionExhibitor { Designers, Brands, Manufacturers, Suppliers, Retailers, Artisans, Students, Unsure }

/// <summary>Refers to whom the event is for.</summary>
internal enum FashionAudience { Trade, Consumer, Mixed, Unknown, Unsure }

/// <summary>Represents an <c>extract-listing</c> answer as the schema states it.</summary>
internal sealed record ExtractListingAnswer(
    string Title,
    ListingOfferType OfferType,
    IReadOnlyList<string> CategoryPath,
    ListingAsk? Price,
    ListingLocation Location,
    ListingItem Item,
    ListingAuthor Author,
    EntityRelation Relation,
    RentalTermsAnswer? RentalTerms,
    IReadOnlyList<PostTagsFlag> Flags);

/// <summary>Refers to what the listing offers.</summary>
internal enum ListingOfferType { Sale, Rent, Service, Unsure }

/// <summary>Represents the asking price, its period for a rent, and the quote it comes from.</summary>
internal sealed record ListingAsk(decimal? Amount, string? Currency, ListingAskPeriod? Period, string Evidence);

/// <summary>Refers to the period a rent price covers.</summary>
internal enum ListingAskPeriod { Hour, Night, Day, Week, Month, Year }

/// <summary>Represents where the offer is.</summary>
internal sealed record ListingLocation(string? District, string? City, string? Address);

/// <summary>Represents the facts that identify the offered thing across ads, for deduplication.</summary>
internal sealed record ListingItem(int? Rooms, decimal? AreaM2, int? Floor, int? Floors, string? Building, string? Brand, string? Model, int? Year);

/// <summary>Represents who offers it, by kind only: contacts stay masked and no name is kept.</summary>
internal sealed record ListingAuthor(ListingAuthorKind Kind);

/// <summary>Refers to who offers the listing.</summary>
internal enum ListingAuthorKind { Owner, Agent, Business, Unknown, Unsure }
