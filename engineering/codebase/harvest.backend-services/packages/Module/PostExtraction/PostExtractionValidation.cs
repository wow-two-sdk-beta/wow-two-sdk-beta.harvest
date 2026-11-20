using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Represents a checked answer: the answer as stored, its relation role and its flags.</summary>
internal sealed record ValidatedExtraction(JsonElement Answer, string Role, IReadOnlyList<PostTagsFlag> Flags);

/// <summary>Checks an extraction answer against the text the model read before anything is stored.</summary>
/// <remarks>
///   - every date, price and rental term keeps its verbatim quote only when the quote appears in the post's title or text
///     (the check of <c>rental-terms@2</c>); a date or price quote may also come from the rendered <c>dates</c>, <c>prices</c>
///     or <c>place</c> the prompt received; otherwise the value becomes unknown and the field gets a flag
///   - a price's quote must state its amount; a stated date's quote must state its day or month (or its year, for a year),
///     unless the source's own dates hold that date; an estimated date's quote is only checked to exist
///   - every unsure pick carries a flag (scfd-entity-model #9); free text is clipped, and a phone, email or handle in it
///     is removed with a <c>contacts</c> flag, so a row holds no post text beyond its quotes
///   - <c>fashion</c> stays only for a primary domain under <c>style-fashion</c>; <c>rentalTerms</c> only for a rent
///     offer whose primary domain is under <c>real-estate</c>
///   - a quote may also come from the listing's rendered <c>attributes</c> (OLX's <c>label: value</c> lines), a source of its
///     own like the other renders; a rental term may quote the title and text or the attribute lines
///   - a landmark keeps its name and type only with a quote; a listing's numbers are clamped to what a listing can hold
///   - a relation other than <c>update</c> carries no changes
/// </remarks>
internal static partial class PostExtractionValidation
{
    internal const int MaxFlags = 20;
    internal const int MaxLandmarks = 10;
    internal const int MaxEvidence = 300;
    internal const string QuoteNotFound = "the quote is not in the post";
    internal const string QuoteTooLong = "the quote is longer than 300 characters";
    internal const string AmountNotQuoted = "the quote does not state the amount";
    internal const string DateNotQuoted = "the quote does not state the date";
    internal const string BadDate = "the date does not match its precision";
    internal const string ContactRemoved = "a contact was removed";
    internal const string ChoseUnsure = "the model chose unsure";

    /// <summary>Checks an <c>extract-event</c> answer.</summary>
    internal static ValidatedExtraction ValidateEvent(ExtractEventAnswer answer, ExtractionCorpus corpus, ExtractionTags tags)
    {
        var flags = ModelFlags(answer.Flags);
        var name = new EventName(Text(answer.Name?.En, 200, "name", flags) ?? "", Text(answer.Name?.Original, 200, "name", flags));

        var occurrences = new List<EventOccurrence>();
        foreach (var occurrence in answer.Occurrences ?? [])
        {
            if (occurrences.Count >= 20 || occurrence is null)
                break;
            if (Evidence(occurrence.Evidence, corpus, "occurrences", flags) is not { } evidence)
                continue;
            if (!IsDate(occurrence.Starts, occurrence.Precision))
            {
                flags.Add(new("occurrences", BadDate, Clip(occurrence.Starts, 40)));
                continue;
            }
            string? ends = occurrence.Ends;
            if (ends is not null && (!IsDate(ends, occurrence.Precision) || string.CompareOrdinal(ends, occurrence.Starts) < 0))
            {
                flags.Add(new("occurrences", BadDate, Clip(ends, 40)));
                ends = null;
            }
            if (occurrence.Certainty == DateCertainty.Stated && !DateQuoted(evidence, occurrence.Starts, ends, occurrence.Precision, corpus))
            {
                flags.Add(new("occurrences", DateNotQuoted, Clip(occurrence.Starts, 40)));
                continue;
            }
            occurrences.Add(occurrence with { Ends = ends, Evidence = evidence });
        }

        var prices = new List<EventPrice>();
        foreach (var price in answer.Prices ?? [])
        {
            if (prices.Count >= 10 || price is null)
                break;
            if (Price(price.Amount, price.Evidence, corpus, "prices", flags) is { } evidence)
                prices.Add(new(price.Amount, Currency(price.Currency), Text(price.Label, 80, "prices", flags), evidence));
        }

        var registration = answer.Registration ?? new EventRegistration(null, null, null);
        registration = new(Date(registration.Opens, corpus, "registration.opens", flags),
            Date(registration.Deadline, corpus, "registration.deadline", flags), Link(registration.Link, flags));

        var participation = (answer.Participation ?? []).Distinct().ToList();
        Unsure(participation.Contains(EventParticipation.Unsure), "participation", flags);
        Unsure(answer.Format == EventFormat.Unsure, "format", flags);
        Unsure(answer.Status == EventStatus.Unsure, "status", flags);

        FashionDetails? fashion = null;
        if (answer.Fashion is { } details && Under(tags.Primary, PostExtractionConstants.FashionDomain))
        {
            var segments = (details.Segments ?? []).Distinct().ToList();
            var exhibitors = (details.ExhibitorProfile ?? []).Distinct().ToList();
            Unsure(segments.Contains(FashionSegment.Unsure), "fashion.segments", flags);
            Unsure(exhibitors.Contains(FashionExhibitor.Unsure), "fashion.exhibitorProfile", flags);
            Unsure(details.Audience == FashionAudience.Unsure, "fashion.audience", flags);
            fashion = new(segments, Slugs(details.GarmentCategories, "fashion.garmentCategories", flags), exhibitors, details.Audience);
        }

        var place = answer.Place ?? new EventPlace(null, null, null);
        var relation = Relation(answer.Relation, flags);
        var validated = answer with
        {
            Name = name,
            EditionLabel = Text(answer.EditionLabel, 40, "editionLabel", flags),
            Occurrences = occurrences,
            Place = new(Text(place.Venue, 120, "place", flags), Text(place.City, 80, "place", flags), Text(place.Country, 80, "place", flags)),
            Organizer = Text(answer.Organizer, 160, "organizer", flags),
            Prices = prices,
            Registration = registration,
            Participation = participation,
            Relation = relation,
            Fashion = fashion,
            Flags = [.. flags.Take(MaxFlags)],
        };
        return new(JsonSerializer.SerializeToElement(validated, EvidenceWire.Options), Name(relation.Role), validated.Flags);
    }

    /// <summary>Checks an <c>extract-listing</c> answer (schema version 2).</summary>
    internal static ValidatedExtraction ValidateListing(ExtractListingAnswer answer, ExtractionCorpus corpus, ExtractionTags tags)
    {
        var flags = ModelFlags(answer.Flags);
        Unsure(answer.PropertyKind == ListingKind.Unsure, "propertyKind", flags);
        Unsure(answer.OfferType == ListingOfferType.Unsure, "offerType", flags);
        Unsure(answer.Furnished == ListingFurnished.Unsure, "furnished", flags);
        Unsure(answer.Author?.Kind == ListingAuthorKind.Unsure, "author", flags);

        ListingAsk? price = null;
        if (answer.Price is { } ask && Price(ask.Amount, ask.Evidence, corpus, "price", flags) is { } evidence)
        {
            price = ask with { Evidence = evidence };
            Unsure(ask.Currency == ListingCurrency.Unsure, "price.currency", flags);
            Unsure(ask.Period == ListingAskPeriod.Unsure, "price.period", flags);
        }

        var category = (answer.CategoryPath ?? []).Select(step => Clip(step?.Trim(), 80)).OfType<string>()
            .Where(step => step.Length > 0).Take(6).ToList();
        var location = answer.Location ?? new ListingLocation(null, null, null);
        var item = Item(answer.Item, flags);

        var apartment = Picks(answer.ApartmentAmenities, ListingApartmentAmenity.Unsure, "apartmentAmenities", flags);
        var nearby = Picks(answer.NearbyAmenities, ListingNearbyAmenity.Unsure, "nearbyAmenities", flags);
        var accepted = Picks(answer.AcceptedTenants, ListingTenant.Unsure, "acceptedTenants", flags);
        var excluded = Picks(answer.ExcludedTenants, ListingTenant.Unsure, "excludedTenants", flags);

        var landmarks = new List<ListingLandmark>();
        foreach (var landmark in answer.Landmarks ?? [])
        {
            if (landmarks.Count >= MaxLandmarks || landmark is null)
                break;
            if (Text(landmark.Name, 120, "landmarks", flags) is not { } name
                || Evidence(landmark.Evidence, corpus, "landmarks", flags) is not { } quote)
                continue;
            Unsure(landmark.Type == ListingLandmarkType.Unsure, "landmarks", flags);
            if (!landmarks.Any(known => known.Type == landmark.Type && known.Name == name))
                landmarks.Add(landmark with { Name = name, Evidence = quote });
        }

        ListingRentalTerms? terms = null;
        if (answer.RentalTerms is { } stated && answer.OfferType == ListingOfferType.Rent
            && Under(tags.Primary, PostExtractionConstants.RealEstateDomain))
        {
            var (source, visible) = corpus.TermSource();
            var (checkedTerms, rejected) = RentalTermEvidence.CheckListingTerms(stated, source, visible);
            terms = checkedTerms;
            flags.AddRange(rejected.Select(field => new PostTagsFlag($"rentalTerms.{field}", "a claim's quote or value did not match the post", null)));
            UnsureClaims(checkedTerms, flags);
        }

        var relation = Relation(answer.Relation, flags);
        var validated = answer with
        {
            Title = Text(answer.Title, 200, "title", flags) ?? "",
            CategoryPath = category,
            Price = price,
            Item = item,
            ApartmentAmenities = apartment,
            NearbyAmenities = nearby,
            Location = new(Text(location.District, 80, "location", flags), Text(location.City, 80, "location", flags),
                Text(location.Address, 160, "location", flags)),
            Landmarks = landmarks,
            Author = new(answer.Author?.Kind ?? ListingAuthorKind.Unknown),
            Relation = relation,
            RentalTerms = terms,
            AcceptedTenants = accepted,
            ExcludedTenants = excluded,
            Flags = [.. flags.Take(MaxFlags)],
        };
        return new(JsonSerializer.SerializeToElement(validated, EvidenceWire.Options), Name(relation.Role), validated.Flags);
    }

    /// <summary>Clamps the offered thing's numbers to what a listing can hold, drops an area above the total, flags a floor above the
    /// building's floors and an unsure pick, and removes a contact from the building's name.</summary>
    private static ListingItem Item(ListingItem? stated, List<PostTagsFlag> flags)
    {
        var item = stated ?? new ListingItem(null, null, null, null, null, new ListingAreas(null, null, null), null, null, null,
            new ListingBathrooms(null, null), null, null);
        var areas = item.AreaM2 ?? new ListingAreas(null, null, null);
        decimal? total = Area(areas.Total), living = Area(areas.Living), kitchen = Area(areas.Kitchen);
        if (total is { } whole)
        {
            if (living > whole)
            {
                flags.Add(new("item.areaM2.living", "an area above the total", living.Value.ToString(CultureInfo.InvariantCulture)));
                living = null;
            }
            if (kitchen > whole)
            {
                flags.Add(new("item.areaM2.kitchen", "an area above the total", kitchen.Value.ToString(CultureInfo.InvariantCulture)));
                kitchen = null;
            }
        }
        int? floor = Range(item.Floor, -5, 300), floors = Range(item.Floors, 1, 300);
        if (floor > floors)
            flags.Add(new("item.floor", "the floor is above the building's floors", floor.Value.ToString(CultureInfo.InvariantCulture)));
        var bathrooms = item.Bathrooms ?? new ListingBathrooms(null, null);
        Unsure(item.PropertyType == ListingMarket.Unsure, "item.propertyType", flags);
        Unsure(item.BuildingType == ListingBuildingType.Unsure, "item.buildingType", flags);
        Unsure(item.Layout == ListingRoomLayout.Unsure, "item.layout", flags);
        Unsure(bathrooms.Type == ListingBathroomType.Unsure, "item.bathrooms.type", flags);
        Unsure(item.Renovation == ListingRenovationLevel.Unsure, "item.renovation", flags);
        return new(item.PropertyType, item.BuildingType, Range(item.YearBuilt, 1800, 2100), Range(item.Rooms, 0, 100), item.Layout,
            new ListingAreas(total, living, kitchen), floor, floors, item.CeilingM is > 0 and <= 20 ? item.CeilingM : null,
            new ListingBathrooms(Range(bathrooms.Count, 0, 20), bathrooms.Type), item.Renovation, Text(item.Building, 80, "item", flags));
    }

    /// <summary>Removes duplicates from a pick list and flags an <c>unsure</c> one.</summary>
    private static List<T> Picks<T>(IReadOnlyList<T>? values, T unsure, string field, List<PostTagsFlag> flags) where T : struct, Enum
    {
        var picks = (values ?? []).Distinct().ToList();
        Unsure(picks.Contains(unsure), field, flags);
        return picks;
    }

    /// <summary>Flags a kept rental claim that holds an <c>unsure</c> currency, unit or billing.</summary>
    private static void UnsureClaims(ListingRentalTerms terms, List<PostTagsFlag> flags)
    {
        Unsure(terms.Deposit.Claims.Any(claim => claim.Value?.Currency == ListingCurrency.Unsure), "rentalTerms.deposit", flags);
        Unsure(terms.AdvanceRent.Claims.Any(claim => claim.Value?.Currency == ListingCurrency.Unsure
            || claim.Value?.Duration?.Unit == ListingDurationUnit.Unsure), "rentalTerms.advanceRent", flags);
        Unsure(terms.Utilities.Claims.Any(claim => claim.Value?.Billing == ListingUtilitiesBilling.Unsure), "rentalTerms.utilities", flags);
        Unsure(terms.MinimumStay.Claims.Any(claim => claim.Value?.Unit == ListingDurationUnit.Unsure), "rentalTerms.minimumStay", flags);
        Unsure(terms.Commission.Claims.Any(claim => claim.Value?.Currency == ListingCurrency.Unsure), "rentalTerms.commission", flags);
    }

    private static decimal? Area(decimal? value) => value is > 0 and <= 1_000_000 ? value : null;

    /// <summary>Whether <paramref name="value"/> is an ISO date at <paramref name="precision"/>: <c>yyyy</c>, <c>yyyy-MM</c>,
    /// <c>yyyy-MM-dd</c>, or <c>yyyy-MM-ddTHH:mm</c> (seconds optional) for a time.</summary>
    internal static bool IsDate(string? value, DatePrecision precision)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        string[] formats = precision switch
        {
            DatePrecision.Year => ["yyyy"],
            DatePrecision.Month => ["yyyy-MM"],
            DatePrecision.Day => ["yyyy-MM-dd"],
            _ => ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"],
        };
        return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            && parsed.Year is >= 1900 and <= 2100;
    }

    /// <summary>Whether a text holds a contact: a phone number of 9 or more digits, an email, a handle or a messenger link.</summary>
    internal static bool HasContact(string value) =>
        Email().IsMatch(value) || Handle().IsMatch(value) || Messenger().IsMatch(value)
        || Phone().Matches(value).Any(match => match.Value.Count(char.IsAsciiDigit) >= 9);

    /// <summary>Returns <paramref name="value"/> with every contact <see cref="HasContact"/> detects replaced by
    /// <c>…</c>: an email, a messenger link, a handle, and a phone number of 9 or more digits; a text with none comes back
    /// unchanged.</summary>
    /// <remarks>The same patterns as <see cref="HasContact"/>, for a text a person reads whole (the relevance review's
    /// title), where <c>Text</c> drops the whole value instead.</remarks>
    internal static string Scrub(string value)
    {
        const string Removed = "…";
        string text = Email().Replace(value, Removed);
        text = Messenger().Replace(text, Removed);
        text = Handle().Replace(text, Removed);
        return Phone().Replace(text, match => match.Value.Count(char.IsAsciiDigit) >= 9 ? Removed : match.Value);
    }

    private static List<PostTagsFlag> ModelFlags(IReadOnlyList<PostTagsFlag>? flags)
    {
        var list = new List<PostTagsFlag>();
        foreach (var flag in flags ?? [])
            if (!string.IsNullOrWhiteSpace(flag?.Field) && list.Count < MaxFlags)
                list.Add(new(Clip(flag.Field.Trim(), 40)!, Clip(flag.Reason?.Trim(), 200) ?? "", Clip(flag.Proposed?.Trim(), 120)));
        return list;
    }

    private static void Unsure(bool unsure, string field, List<PostTagsFlag> flags)
    {
        if (unsure && !flags.Any(flag => flag.Field == field || flag.Field.StartsWith(field + ".", StringComparison.Ordinal)))
            flags.Add(new(field, ChoseUnsure, null));
    }

    private static string? Evidence(string? quote, ExtractionCorpus corpus, string field, List<PostTagsFlag> flags)
    {
        if (quote is { Length: > MaxEvidence })
        {
            flags.Add(new(field, QuoteTooLong, null));
            return null;
        }
        if (RentalTermEvidence.Quoted(quote, corpus.Text, corpus.Visible)
            || corpus.Structured.Any(render => RentalTermEvidence.Quoted(quote, render, render.Length)))
            return quote;
        flags.Add(new(field, QuoteNotFound, null));
        return null;
    }

    /// <summary>Whether a stated date is backed: its quote states the date or its end, or the source's own <c>dates</c> hold
    /// the value (the prompt may tell the model to quote the title for a date found only there).</summary>
    private static bool DateQuoted(string quote, string value, string? end, DatePrecision precision, ExtractionCorpus corpus) =>
        RentalTermEvidence.StatesDate(quote, value, precision)
        || (end is not null && RentalTermEvidence.StatesDate(quote, end, precision))
        || (corpus.Dates is { } dates && (RentalTermEvidence.Quoted(value, dates, dates.Length)
            || (end is not null && RentalTermEvidence.Quoted(end, dates, dates.Length))));

    private static string? Price(decimal? amount, string? quote, ExtractionCorpus corpus, string field, List<PostTagsFlag> flags)
    {
        if (Evidence(quote, corpus, field, flags) is not { } evidence)
            return null;
        if (amount is { } value && (value < 0 || !RentalTermEvidence.StatesAmount(evidence, value)))
        {
            flags.Add(new(field, AmountNotQuoted, value.ToString(CultureInfo.InvariantCulture)));
            return null;
        }
        return evidence;
    }

    private static EvidencedDate? Date(EvidencedDate? date, ExtractionCorpus corpus, string field, List<PostTagsFlag> flags)
    {
        if (date is null || Evidence(date.Evidence, corpus, field, flags) is not { } evidence)
            return null;
        if (!IsDate(date.Value, date.Precision))
        {
            flags.Add(new(field, BadDate, Clip(date.Value, 40)));
            return null;
        }
        if (!DateQuoted(evidence, date.Value, null, date.Precision, corpus))
        {
            flags.Add(new(field, DateNotQuoted, Clip(date.Value, 40)));
            return null;
        }
        return date with { Evidence = evidence };
    }

    private static string? Link(string? link, List<PostTagsFlag> flags)
    {
        if (string.IsNullOrWhiteSpace(link))
            return null;
        if (link.Length <= 500 && Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return uri.ToString();
        flags.Add(new("registration.link", "not a web link", null));
        return null;
    }

    private static EntityRelation Relation(EntityRelation? relation, List<PostTagsFlag> flags)
    {
        relation ??= new(RelationRole.Unsure, [], null);
        Unsure(relation.Role == RelationRole.Unsure, "relation.role", flags);
        if (relation.Role != RelationRole.Update)
            return relation with { Changes = [], Status = null };
        var changes = (relation.Changes ?? []).Distinct().ToList();
        if (relation.Status is not null && !changes.Contains(RelationChange.Status))
            changes.Add(RelationChange.Status);
        if (changes.Contains(RelationChange.Status) && relation.Status is null)
            flags.Add(new("relation.status", "a status change without its status", null));
        Unsure(changes.Contains(RelationChange.Unsure), "relation.changes", flags);
        return relation with { Changes = changes };
    }

    private static List<string> Slugs(IReadOnlyList<string>? values, string field, List<PostTagsFlag> flags)
    {
        var slugs = new List<string>();
        foreach (var raw in values ?? [])
        {
            if (PostTagsValidation.Slug(raw) is { } slug)
            {
                if (!slugs.Contains(slug) && slugs.Count < 10)
                    slugs.Add(slug);
            }
            else if (!string.IsNullOrWhiteSpace(raw))
                flags.Add(new(field, PostTagsValidation.NotEnglish, Clip(raw.Trim(), 120)));
        }
        return slugs;
    }

    private static string? Text(string? value, int max, string field, List<PostTagsFlag> flags)
    {
        string? text = Clip(value?.Trim(), max);
        if (string.IsNullOrEmpty(text))
            return null;
        if (!HasContact(text))
            return text;
        if (!flags.Any(flag => flag.Field == "contacts" && flag.Proposed == field))
            flags.Add(new("contacts", ContactRemoved, field));
        return null;
    }

    private static string? Currency(string? currency)
    {
        string? code = currency?.Trim().ToUpperInvariant();
        return code is { Length: 3 } && code.All(char.IsAsciiLetterUpper) ? code : null;
    }

    private static int? Range(int? value, int min, int max) => value is { } number && number >= min && number <= max ? number : null;

    private static bool Under(string? node, string key) => node is not null && PostExtractionDemand.AtOrUnder(node, key);

    private static string Name(RelationRole role) => role.ToString().ToLowerInvariant();

    private static string? Clip(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..(char.IsHighSurrogate(value[max - 1]) ? max - 1 : max)];

    [GeneratedRegex(@"[+\d][\d\s\-().]{7,}\d", RegexOptions.CultureInvariant)]
    private static partial Regex Phone();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    [GeneratedRegex(@"(?<![\w.])@[A-Za-z0-9_]{4,}", RegexOptions.CultureInvariant)]
    private static partial Regex Handle();

    [GeneratedRegex(@"\b(?:t\.me|wa\.me|telegram\.me)/\S*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Messenger();
}
