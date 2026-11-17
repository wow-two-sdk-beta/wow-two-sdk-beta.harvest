using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;

/// <summary>Normalizes known native housing fields while preserving their source evidence.</summary>
public static partial class ListingSchemaMapper
{
    /// <summary>Maps a housing post using the current deterministic rules.</summary>
    /// <param name="envelope">The extracted post and native source fields.</param>
    /// <param name="sourceUrl">The source link, when available.</param>
    /// <returns>The listing projection, or null when the post has no recognized housing evidence.</returns>
    public static JsonElement? Map(PostEnvelope envelope, string? sourceUrl = null) =>
        Project(envelope, sourceUrl)?.ToJson();

    internal static ListingProjection? ReadOrProject(PostEnvelope envelope, string? sourceUrl) =>
        envelope.Extras.TryGetValue("listing", out var stored) && ListingProjection.Read(stored) is { } known
            ? known : Project(envelope, sourceUrl);

    internal static PostEnvelope WithProjection(PostEnvelope envelope, string? sourceUrl)
    {
        if (envelope.Extras.ContainsKey("listing") || Map(envelope, sourceUrl) is not { } mapped) return envelope;
        var extras = new Dictionary<string, JsonElement>(envelope.Extras) { ["listing"] = mapped };
        var sources = new Dictionary<string, FieldSource>(envelope.FieldSources)
        {
            ["listing"] = new() { Tier = ExtractionTier.Deterministic, Rule = RuleVersion },
        };
        return envelope with { Extras = extras, FieldSources = sources };
    }

    private const string RuleVersion = "housing-listing@1";

    private static ListingProjection? Project(PostEnvelope envelope, string? sourceUrl)
    {
        if (envelope.Kind != PostKind.Listing || !envelope.Extras.TryGetValue("source", out var native)
            || Child(native, "fields") is not { ValueKind: JsonValueKind.Object } fields)
            return null;
        var state = new MappingInput(fields);
        string? sourceSchema = ReaderSupport.String(native, "schema");
        var projection = sourceSchema switch
        {
            "olx.ad.v1" => Olx(state, envelope, sourceUrl),
            "joymee.ad.v1" => Joymee(state, envelope),
            _ => null,
        };
        return projection is null ? null : projection with
        {
            Mapping = new(sourceSchema!, ReaderSupport.String(native, "parserVersion"), RuleVersion),
            Evidence = state.Evidence, UnknownValues = state.Unknown, QualityFlags = state.Flags.Order(StringComparer.Ordinal).ToArray(),
        };
    }

    private static ListingProjection? Olx(MappingInput input, PostEnvelope envelope, string? sourceUrl)
    {
        if (Boolean(input.Native("isJob")?.Raw) is true) return null;
        var category = input.Native("categoryPath");
        if (category is null && Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) && HousingKind(uri.AbsolutePath) is not null)
            category = new(uri.AbsolutePath, "/link/url");
        var route = category?.Raw;
        var kind = HousingKind(route);
        if (kind is { } && category is { }) input.Record("/property/kind", category, "categoryPath");
        if (kind is null)
        {
            var apartment = input.Attribute("В квартире есть", "Kvartirada bor");
            if (apartment is null && input.Attribute("Этаж", "Qavat") is not null)
                apartment = input.Attribute("Этажность дома", "Qavatlar soni");
            if (apartment is null) return null;
            kind = ListingPropertyKind.Apartment;
            input.Record("/property/kind", apartment, "categoryFields");
        }

        var layout = input.Attribute("Планировка", "Rejalashtirish", "Joylashuvi");
        var connectivity = ParseAlias(layout?.Raw, Connectivity);
        var style = ParseAlias(layout?.Raw, Layouts);
        if (connectivity is not null) input.Record("/unit/roomConnectivity", layout, "sourceLabel");
        else if (style is not null) input.Record("/unit/layout", layout, "sourceLabel");
        else input.Reject("/unit/layout", layout, "unmappedLabel");

        var bath = input.Attribute("Санузел", "Sanuzel");
        var bathroomsMin = AliasKey(bath?.Raw) is "2 санузла и более" or "2 и более" ? (short?)2 : null;
        var arrangement = bathroomsMin is null ? input.Enum("/unit/bathroomArrangement", bath, Bathrooms) : null;
        if (bathroomsMin is not null) input.Record("/unit/bathroomsMin", bath, "lowerBound");

        var finish = input.Attribute("Ремонт", "Ta’mir", "Ta'mir", "Ta'mirlash");
        var stage = ParseAlias(finish?.Raw, Finishes);
        var renovation = ParseAlias(finish?.Raw, Renovations);
        if (stage is not null) input.Record("/property/finishing", finish, "sourceLabel");
        else if (renovation is not null) input.Record("/property/renovation", finish, "sourceLabel");
        else input.Reject("/property/renovation", finish, "unmappedLabel");

        var unit = new ListingUnit(
            Rooms: input.Count("/unit/rooms", input.Attribute("Количество комнат", "Xonalar soni", "Xonalar"), 0, 250),
            Floor: input.Count("/unit/floor", input.Attribute("Этаж", "Qavat"), -20, 300),
            BuildingFloors: input.Count("/unit/buildingFloors", input.Attribute("Этажность дома", "Qavatlar soni"), 1, 300),
            TotalAreaM2: input.Area("/unit/totalAreaM2", input.Attribute("Общая площадь", "Общая площадь, м²", "Umumiy maydon", "Maydon")),
            LivingAreaM2: input.Area("/unit/livingAreaM2", input.Attribute("Жилая площадь", "Yashash maydoni")),
            KitchenAreaM2: input.Area("/unit/kitchenAreaM2", input.Attribute("Площадь кухни", "Oshxona maydoni")),
            LotAreaM2: input.LotArea(input.Attribute("Площадь участка", "Yer maydoni", "Uchastka maydoni")),
            CeilingHeightM: input.Ceiling(input.Attribute("Высота потолков", "Shift balandligi")),
            BathroomArrangement: arrangement,
            BathroomsMin: bathroomsMin,
            Furnishing: input.Furnishing(input.Attribute("Меблирована", "Меблирован", "Mebelli")),
            Layout: style,
            RoomConnectivity: connectivity);
        input.CheckMeasurements(unit, kind.Value);
        var transaction = TransactionFromRoute(route);
        if (transaction is not null) input.Record("/offers/0/transaction", category, "categoryPath");
        var current = OlxPrice(input, envelope);
        var negotiable = input.Bool("/offers/0/negotiable", input.Native("isNegotiable") ?? input.Native("price", "negotiable"));
        var commission = input.Bool("/offers/0/hasCommission", input.Attribute("Комиссионные", "Komissiya"));
        var offers = current is not null || transaction is not null || commission is not null || negotiable is not null
            ? new[] { new ListingOffer(transaction, PricePeriod(input), current, null, negotiable, commission) } : [];

        var publisherFlag = input.Native("isBusiness");
        var business = Boolean(publisherFlag?.Raw);
        var sellerBadge = input.Attribute("Бизнес", "Частное лицо", "Biznes", "Jismoniy shaxs");
        ListingAccountClass? account = business switch
        {
            true => ListingAccountClass.Business,
            false => ListingAccountClass.PrivateIndividual,
            _ => ParseAlias(sellerBadge?.Raw, PublisherClasses),
        };
        if (account is not null) input.Record("/publisher/accountClass", business is null ? sellerBadge : publisherFlag, "sourceField");
        return new()
        {
            Property = new(kind,
                input.Enum("/property/marketSegment", input.Attribute("Тип жилья", "Uy-joy turi"), MarketSegments),
                input.Enum("/property/construction", input.Attribute("Тип строения", "Материал стен", "Qurilish turi", "Devor materiali"), Construction),
                input.Year(input.Attribute("Год постройки", "Qurilgan yili"), input.Attribute("Год постройки/сдачи")),
                stage, renovation),
            Unit = unit,
            Offers = offers,
            Place = input.Place(input.Native("location", "cityName"), input.Native("location", "districtName"),
                null, input.Native("map", "lat"), input.Native("map", "lon")),
            Features = input.Features(),
            Publisher = account is null ? null : new(account, null),
            Mapping = new("olx.ad.v1", null, RuleVersion),
        };
    }

    private static ListingProjection? Joymee(MappingInput input, PostEnvelope envelope)
    {
        var rawKind = input.Native("propertyType") ?? input.Attribute("Тип объекта");
        if (rawKind is { Raw: not ("1" or "Квартира") }) return null;
        var kind = rawKind is null ? (ListingPropertyKind?)null : ListingPropertyKind.Apartment;
        input.Record("/property/kind", rawKind, "sourceField");
        var rawDeal = input.Native("dealType") ?? input.Attribute("Тип сделки");
        var rent = rawDeal is { Raw: "2" or "Долгосрочная аренда" } ? (ListingTransaction?)ListingTransaction.Rent : null;
        if (rent is not null) input.Record("/offers/0/transaction", rawDeal, "sourceField");
        else input.Reject("/offers/0/transaction", rawDeal, "providerCode");

        input.Reject("/property/renovation", input.Native("detail", "repair"), "providerCode");
        input.Reject("/property", input.Native("detail", "apartment_type"), "providerCode");
        input.Reject("/publisher/accountClass", input.Native("advertiserType"), "providerCode");
        var unit = new ListingUnit(
            Rooms: input.Count("/unit/rooms", input.Native("detail", "room_quantity") ?? input.Attribute("Количество комнат"), 0, 250),
            Floor: input.Count("/unit/floor", input.Native("detail", "floor_number") ?? input.Attribute("Этаж"), -20, 300),
            BuildingFloors: input.Count("/unit/buildingFloors", input.Native("detail", "floors_count") ?? input.Attribute("Этажность дома"), 1, 300),
            TotalAreaM2: input.Area("/unit/totalAreaM2", input.Native("detail", "area_m2") ?? input.Attribute("Общая площадь, м²")));
        input.CheckMeasurements(unit, kind);
        var currencyCode = input.Native("pricing", "currency");
        var currency = currencyCode?.Raw switch { "1" => "UZS", "2" => "USD", _ => null };
        if (currency is null) input.Reject("/offers/0/price/currency", currencyCode, "providerCode");
        else input.Record("/offers/0/price/currency", currencyCode, "providerCode");
        var amount = input.Native("pricing", "price");
        var current = Money(input, envelope, amount, currency, input.Native("priceText"),
            input.Native("pricing", "price_from"), input.Native("pricing", "price_to"));
        var previous = input.Native("previousPrice");
        var previousAmount = input.Price("/offers/0/previousPrice/amount", previous);
        var prior = previousAmount is null ? null : new ListingMoney(previousAmount, null, previous?.Raw);
        var verified = input.Bool("/publisher/verified", input.Native("publisherVerified"));
        return new()
        {
            Property = new(kind), Unit = unit,
            Offers = rent is not null || current is not null || prior is not null ? [new ListingOffer(rent, PricePeriod(input), current, prior)] : [],
            Place = input.Place(input.Native("city"), input.Native("district"), input.Native("addressLine"),
                input.Native("location", "latitude"), input.Native("location", "longitude")),
            Publisher = verified is null ? null : new(null, verified),
            Mapping = new("joymee.ad.v1", null, RuleVersion),
        };
    }

    private static ListingMoney? OlxPrice(MappingInput input, PostEnvelope envelope)
    {
        var providerCurrency = input.Native("price", "regularPrice", "currencyCode");
        var currency = Currency(providerCurrency?.Raw);
        if (currency is not null) input.Record("/offers/0/price/currency", providerCurrency, "sourceField");
        else input.Reject("/offers/0/price/currency", providerCurrency, "unknownCurrency");
        var current = Money(input, envelope, input.Native("price", "regularPrice", "value"), currency, input.Native("priceText"));
        if (current?.Amount == 0 && Boolean(input.Native("price", "free")?.Raw) is not true
            && !IsFreeText(current.Raw))
        {
            input.Flag("unconfirmedZeroPrice");
            return current with { Amount = null };
        }
        return current;
    }

    private static ListingMoney? Money(MappingInput input, PostEnvelope envelope, NativeValue? amountValue, string? currency,
        NativeValue? display, NativeValue? minValue = null, NativeValue? maxValue = null)
    {
        decimal? amount = input.Price("/offers/0/price/amount", amountValue);
        decimal? min = input.Price("/offers/0/price/min", minValue);
        decimal? max = input.Price("/offers/0/price/max", maxValue);
        if (min is { } low && max is { } high && low > high) input.Flag("priceRangeReversed");

        var structured = envelope.Extras.TryGetValue("schemaOrg", out var product) ? Child(product, "offers") : default;
        if (structured.ValueKind == JsonValueKind.Array) structured = structured.EnumerateArray().FirstOrDefault();
        var structuredPrice = Value(Child(structured, "price"), "/post/extras/schemaOrg/offers/price");
        var structuredCurrency = Value(Child(structured, "priceCurrency"), "/post/extras/schemaOrg/offers/priceCurrency");
        if (amount is null) amount = input.Price("/offers/0/price/amount", structuredPrice);
        else if (Decimal(structuredPrice?.Raw) is { } second && second != amount)
            input.Conflict("priceAmountConflict", "/offers/0/price/amount", structuredPrice);
        if (currency is null && Currency(structuredCurrency?.Raw) is { } explicitCurrency)
        {
            currency = explicitCurrency;
            input.Record("/offers/0/price/currency", structuredCurrency, "sourceField");
        }
        else if (currency is not null && Currency(structuredCurrency?.Raw) is { } other && other != currency)
            input.Conflict("priceCurrencyConflict", "/offers/0/price/currency", structuredCurrency);

        var match = display is null ? Match.Empty : DisplayPrice().Match(display.Raw);
        if (match.Success && Decimal(match.Groups["amount"].Value) is { } visible)
        {
            if (amount is null && min is null && max is null && visible is >= 0 and <= 99999999999999.999999m)
            {
                amount = visible;
                input.Record("/offers/0/price/amount", display, "displayPrice");
            }
            else if (amount is not null && visible != amount)
                input.Conflict("priceAmountConflict", "/offers/0/price/amount", display);
            var displayCurrency = Currency(match.Groups["currency"].Value);
            if (currency is null && displayCurrency is not null)
            {
                currency = displayCurrency;
                input.Record("/offers/0/price/currency", display, "displayPrice");
            }
            else if (currency is not null && displayCurrency is not null && currency != displayCurrency)
                input.Conflict("priceCurrencyConflict", "/offers/0/price/currency", display);
        }
        if (display is not null) input.Record("/offers/0/price/raw", display, "sourceField");
        if (currency is null && amount is not null && display is not null)
            input.Reject("/offers/0/price/currency", display, "unresolvedCurrency");
        return amount is not null || min is not null || max is not null || display is not null
            ? new(amount, currency, display?.Raw, min, max) : null;
    }

    private static ListingPeriod? PricePeriod(MappingInput input)
    {
        var stated = input.Native("pricing", "period") ?? input.Attribute("Период аренды");
        return input.Enum("/offers/0/period", stated, Periods);
    }

    private static bool IsFreeText(string? raw) => AliasKey(raw) is "бесплатно" or "bepul";

    private static ListingPropertyKind? HousingKind(string? path)
    {
        if (path is null) return null;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (!parts.Contains("nedvizhimost") && !parts.Contains("kochmas-mulk")) return null;
        if (parts.Any(part => part is "kvartiry" or "kvartiralar")) return ListingPropertyKind.Apartment;
        if (parts.Any(part => part is "doma" or "uylar")) return ListingPropertyKind.House;
        if (parts.Any(part => part is "komnaty" or "xonalar")) return ListingPropertyKind.Room;
        if (parts.Any(part => part is "zemlya" or "yer-uchastkalari")) return ListingPropertyKind.Land;
        return null;
    }

    private static ListingTransaction? TransactionFromRoute(string? path)
    {
        var parts = path?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (parts.Any(part => part is "arenda-dolgosrochnaya" or "arenda" or "posutochno_pochasovo" or "ijara"))
            return ListingTransaction.Rent;
        return parts.Any(part => part is "prodazha" or "sotish" or "sotuv") ? ListingTransaction.Sale : null;
    }

    private static string? Currency(string? raw) => AliasKey(raw) switch
    {
        "usd" or "$" => "USD",
        "uzs" or "so'm" or "сум" or "сўм" => "UZS",
        "eur" or "€" => "EUR",
        "rub" or "₽" => "RUB",
        "kzt" or "₸" => "KZT",
        _ => null,
    };

    private static bool? Boolean(string? value) => AliasKey(value) switch
    {
        "true" or "да" or "ha" => true,
        "false" or "нет" or "yo'q" => false,
        _ => null,
    };

    private static JsonElement Child(JsonElement node, string key) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value) ? value : default;

    private static NativeValue? Value(JsonElement element, string path) => element.ValueKind switch
    {
        JsonValueKind.String when !string.IsNullOrWhiteSpace(element.GetString()) => new(element.GetString()!, path),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => new(element.GetRawText(), path),
        _ => null,
    };

    private static string Pointer(string segment) => segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static string AliasKey(string? value) => Whitespace().Replace((value ?? string.Empty).Trim().ToLowerInvariant(), " ")
        .Replace('ё', 'е').Replace('’', '\'').Replace('‘', '\'').Replace('ʻ', '\'').Replace('ʼ', '\'');

    private static decimal? Decimal(string? raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (!Quantity().IsMatch(value)) return null;
        var cleaned = Whitespace().Replace(value, string.Empty).Replace(',', '.');
        return decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var amount) ? amount : null;
    }

    private static T? ParseAlias<T>(string? value, IReadOnlyDictionary<string, T> choices) where T : struct =>
        choices.TryGetValue(AliasKey(value), out var result) ? result : null;

    private sealed record NativeValue(string Raw, string Path);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^[+-]?(?:\d{1,3}(?:\s\d{3})+|\d+)(?:[.,]\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Quantity();

    [GeneratedRegex(@"^\s*(?<amount>[+-]?(?:\d{1,3}(?:\s\d{3})+|\d+)(?:[.,]\d+)?)\s*(?<currency>\$|€|₽|₸|[^\d/]+)?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex DisplayPrice();
}
