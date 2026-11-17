using System.Text.Json;
using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;

public static partial class ListingSchemaMapper
{
    private sealed class MappingInput(JsonElement fields)
    {
        private readonly Dictionary<string, NativeValue> attributes = ReadAttributes(fields);

        internal List<ListingEvidence> Evidence { get; } = [];
        internal List<ListingUnknownValue> Unknown { get; } = [];
        internal HashSet<string> Flags { get; } = new(StringComparer.Ordinal);

        internal NativeValue? Native(params string[] keys)
        {
            var node = fields;
            foreach (var key in keys) node = Child(node, key);
            return Value(node, "/fields/" + string.Join('/', keys.Select(Pointer)));
        }

        internal NativeValue? Attribute(params string[] aliases)
        {
            foreach (var alias in aliases)
                if (attributes.TryGetValue(AliasKey(alias), out var found)) return found;
            return null;
        }

        internal void Record(string path, NativeValue? input, string method)
        {
            if (input is not null) Evidence.Add(new(path, input.Path, input.Raw, method));
        }

        internal void Reject(string path, NativeValue? input, string reason)
        {
            if (input is not null) Unknown.Add(new(path, input.Path, input.Raw, reason));
        }

        internal void Flag(string reason) => Flags.Add(reason);

        internal void Conflict(string reason, string path, NativeValue? input)
        {
            Flag(reason);
            Record(path, input, "conflictingValue");
        }

        internal T? Enum<T>(string path, NativeValue? input, IReadOnlyDictionary<string, T> choices) where T : struct
        {
            var selected = ParseAlias(input?.Raw, choices);
            if (selected is null) Reject(path, input, "unmappedLabel");
            else Record(path, input, "sourceLabel");
            return selected;
        }

        internal bool? Bool(string path, NativeValue? input)
        {
            var selected = Boolean(input?.Raw);
            if (selected is null) Reject(path, input, "unmappedLabel");
            else Record(path, input, "sourceField");
            return selected;
        }

        internal ListingFurnishing? Furnishing(NativeValue? input)
        {
            var path = "/unit/furnishing";
            return Bool(path, input) switch
            {
                true => ListingFurnishing.Furnished,
                false => ListingFurnishing.Unfurnished,
                _ => null,
            };
        }

        internal short? Count(string path, NativeValue? input, short min, short max)
        {
            var value = Decimal(input?.Raw);
            if (value is not { } number || number < min || number > max || number != decimal.Truncate(number))
            {
                Reject(path, input, "invalidCount");
                return null;
            }
            Record(path, input, "sourceField");
            return (short)number;
        }

        internal decimal? Area(string path, NativeValue? input)
        {
            if (input is null) return null;
            var value = Decimal(AreaSuffix().Replace(input.Raw, string.Empty));
            if (value is not > 0 or > 9999999999.9999m)
            {
                Reject(path, input, "invalidArea");
                return null;
            }
            Record(path, input, "squareMetres");
            return value;
        }

        internal decimal? LotArea(NativeValue? input)
        {
            const string path = "/unit/lotAreaM2";
            if (input is null) return null;
            if (AreaSuffix().IsMatch(input.Raw)) return Area(path, input);
            var match = SotkaSuffix().Match(input.Raw);
            if (match.Success && Decimal(input.Raw[..match.Index]) is { } sotka && sotka is > 0 and <= 99999999.9999m)
            {
                Record(path, input, "sotkaToSquareMetres");
                return 100 * sotka;
            }
            Reject(path, input, "ambiguousLotUnit");
            return null;
        }

        internal decimal? Ceiling(NativeValue? input)
        {
            const string path = "/unit/ceilingHeightM";
            if (input is null) return null;
            var match = LengthSuffix().Match(input.Raw);
            var rawUnit = AliasKey(match.Value);
            var number = Decimal(match.Success ? input.Raw[..match.Index] : input.Raw);
            var assumed = !match.Success;
            decimal? metres = rawUnit switch
            {
                "m" or "м" => number,
                "cm" or "см" => number / 100,
                "mm" or "мм" => number / 1000,
                _ when number is >= 2 and <= 6 => number,
                _ when number is >= 200 and <= 600 => number / 100,
                _ => null,
            };
            if (metres is not >= 1.5m or > 15m)
            {
                Reject(path, input, assumed ? "ambiguousCeilingUnit" : "invalidCeilingHeight");
                return null;
            }
            Record(path, input, assumed ? "unitConvention" : "explicitUnits");
            return metres;
        }

        internal decimal? Price(string path, NativeValue? input)
        {
            if (input is null) return null;
            var value = Decimal(input.Raw);
            if (value is not >= 0 or > 99999999999999.999999m)
            {
                Reject(path, input, "invalidPriceAmount");
                return null;
            }
            Record(path, input, "sourceField");
            return value;
        }

        internal ListingBuildYear? Year(NativeValue? built, NativeValue? builtOrHandover)
        {
            const string path = "/property/buildYear";
            var input = built ?? builtOrHandover;
            if (input is null) return null;
            var text = AliasKey(input.Raw);
            var match = Years().Match(text);
            if (!match.Success || !short.TryParse(match.Groups["from"].Value, out var first) || first is < 1000 or > 2200)
            {
                Reject(path, input, "invalidYear");
                return null;
            }
            var role = built is null ? ListingBuildYearRole.BuiltOrHandover : ListingBuildYearRole.Built;
            var before = match.Groups["before"].Success;
            short? last = before ? (short)(first - 1) : short.TryParse(match.Groups["to"].Value, out short end) ? end : first;
            if (!before && (last < first || last > 2200))
            {
                Reject(path, input, "invalidYearRange");
                return null;
            }
            if (built is not null && builtOrHandover is not null && built.Raw != builtOrHandover.Raw)
                Conflict("buildYearConflict", path, builtOrHandover);
            Record(path, input, before ? "upperBound" : "sourceField");
            return new(before ? null : first, last, role);
        }

        internal ListingPlace? Place(NativeValue? city, NativeValue? district, NativeValue? address,
            NativeValue? latitude, NativeValue? longitude)
        {
            var lat = Decimal(latitude?.Raw);
            var lon = Decimal(longitude?.Raw);
            var hasPoint = lat is >= -90 and <= 90 && lon is >= -180 and <= 180 && (lat != 0 || lon != 0);
            if (hasPoint)
            {
                Record("/place/latitude", latitude, "sourceMapPoint");
                Record("/place/longitude", longitude, "sourceMapPoint");
            }
            else
            {
                Reject("/place/latitude", latitude, "invalidMapPoint");
                Reject("/place/longitude", longitude, "invalidMapPoint");
            }
            Record("/place/city", city, "sourceField");
            Record("/place/district", district, "sourceField");
            Record("/place/address", address, "sourceField");
            return hasPoint || city is not null || district is not null || address is not null
                ? new(city?.Raw, district?.Raw, address?.Raw, hasPoint ? lat : null, hasPoint ? lon : null) : null;
        }

        internal void CheckMeasurements(ListingUnit unit, ListingPropertyKind? property)
        {
            if (unit.Floor is { } floor && unit.BuildingFloors is { } floors && floor > floors)
                Flag("floorExceedsBuildingFloors");
            if (unit.TotalAreaM2 is { } area && unit.LivingAreaM2 > area) Flag("livingAreaExceedsTotalArea");
            if (unit.TotalAreaM2 is { } total && unit.LivingAreaM2 + unit.KitchenAreaM2 > total)
                Flag("livingPlusKitchenExceedsTotalArea");
            if (property == ListingPropertyKind.Apartment && unit.TotalAreaM2 > 5000) Flag("implausibleApartmentArea");
        }

        internal ListingFeature[] Features()
        {
            var results = new List<ListingFeature>();
            AddFeatures(results, Attribute("В квартире есть", "Kvartirada bor"), ListingFeatureScope.Unit, UnitFeatures);
            AddFeatures(results, Attribute("Рядом есть", "Yaqinida"), ListingFeatureScope.Nearby, NearbyFeatures);
            AddFeatures(results, Attribute("Коммуникации"), ListingFeatureScope.Building, UtilityFeatures);
            return results.ToArray();
        }

        private void AddFeatures(List<ListingFeature> results, NativeValue? input, ListingFeatureScope scope,
            IReadOnlyDictionary<string, string> labels)
        {
            if (input is null) return;
            var tokens = AliasKey(input.Raw).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var missing = false;
            for (int i = 0; i < tokens.Length; i++)
            {
                var pair = i + 1 < tokens.Length ? tokens[i] + ", " + tokens[i + 1] : string.Empty;
                if (labels.TryGetValue(pair, out var code)) i++;
                else if (!labels.TryGetValue(tokens[i], out code))
                {
                    missing = true;
                    continue;
                }
                if (results.Any(feature => feature.Scope == scope && feature.Code == code)) continue;
                Record($"/features/{results.Count}", input, "sourceLabel");
                results.Add(new(code, scope, true));
            }
            if (missing) Reject("/features", input, "partiallyUnmappedFeatureLabels");
        }

        private static Dictionary<string, NativeValue> ReadAttributes(JsonElement fields)
        {
            var attributes = new Dictionary<string, NativeValue>(StringComparer.Ordinal);
            var node = Child(fields, "attributes");
            if (node.ValueKind != JsonValueKind.Object) return attributes;
            foreach (var item in node.EnumerateObject())
                if (Value(item.Value, "/fields/attributes/" + Pointer(item.Name)) is { } value)
                    attributes.TryAdd(AliasKey(item.Name), value);
            return attributes;
        }
    }

    [GeneratedRegex(@"\s*(?:м[²2]|m[²2]|кв\.\s*м|kv\.?\s*m)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AreaSuffix();

    [GeneratedRegex(@"\s*(?:соток|сотка|сотки|sotix|sotka)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SotkaSuffix();

    [GeneratedRegex(@"\s*(?:[cс][mм]|[mм][mм]|[mм])\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LengthSuffix();

    [GeneratedRegex(@"^(?:(?<before>старше|до)\s+)?(?<from>\d{4})(?:\s*[-–—]\s*(?<to>\d{4}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Years();

    private static Dictionary<string, T> Aliases<T>(params (string Native, T Value)[] entries) =>
        entries.ToDictionary(entry => AliasKey(entry.Native), entry => entry.Value, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, ListingMarketSegment> MarketSegments = Aliases(
        ("Новостройки", ListingMarketSegment.NewBuild), ("Новостройка", ListingMarketSegment.NewBuild),
        ("Вторичный рынок", ListingMarketSegment.Resale), ("Ikkilamchi bozor", ListingMarketSegment.Resale));

    private static readonly IReadOnlyDictionary<string, ListingConstruction> Construction = Aliases(
        ("Кирпичный", ListingConstruction.Brick), ("Панельный", ListingConstruction.Panel), ("Монолитный", ListingConstruction.Monolith),
        ("Блочный", ListingConstruction.Block), ("Деревянный", ListingConstruction.Wood),
        ("G'ishtli", ListingConstruction.Brick), ("Panel", ListingConstruction.Panel), ("Monolit", ListingConstruction.Monolith));

    private static readonly IReadOnlyDictionary<string, ListingRoomConnectivity> Connectivity = Aliases(
        ("Раздельная", ListingRoomConnectivity.Separate), ("Смежная", ListingRoomConnectivity.Adjacent),
        ("Смежно-раздельная", ListingRoomConnectivity.Mixed), ("Alohida", ListingRoomConnectivity.Separate));

    private static readonly IReadOnlyDictionary<string, ListingLayout> Layouts = Aliases(
        ("Студия", ListingLayout.Studio), ("Studiya", ListingLayout.Studio), ("Многоуровневая", ListingLayout.MultiLevel),
        ("Пентхаус", ListingLayout.Penthouse), ("Малосемейка", ListingLayout.CompactFamily));

    private static readonly IReadOnlyDictionary<string, ListingFinishing> Finishes = Aliases(
        ("Черновая отделка", ListingFinishing.Shell), ("Предчистовая отделка", ListingFinishing.PreFinish),
        ("Чистовая отделка", ListingFinishing.Finished));

    private static readonly IReadOnlyDictionary<string, ListingRenovation> Renovations = Aliases(
        ("Евроремонт", ListingRenovation.Euro), ("Yevrota'mir", ListingRenovation.Euro),
        ("Авторский проект", ListingRenovation.Designer), ("Средний", ListingRenovation.Average),
        ("Требует ремонта", ListingRenovation.NeedsRepair));

    private static readonly IReadOnlyDictionary<string, ListingBathroomArrangement> Bathrooms = Aliases(
        ("Раздельный", ListingBathroomArrangement.Separate), ("Совмещенный", ListingBathroomArrangement.Combined));

    private static readonly IReadOnlyDictionary<string, ListingAccountClass> PublisherClasses = Aliases(
        ("Бизнес", ListingAccountClass.Business), ("Biznes", ListingAccountClass.Business),
        ("Частное лицо", ListingAccountClass.PrivateIndividual), ("Jismoniy shaxs", ListingAccountClass.PrivateIndividual));

    private static readonly IReadOnlyDictionary<string, ListingPeriod> Periods = Aliases(
        ("month", ListingPeriod.Month), ("месяц", ListingPeriod.Month), ("oyiga", ListingPeriod.Month),
        ("day", ListingPeriod.Day), ("сутки", ListingPeriod.Day), ("kuniga", ListingPeriod.Day),
        ("night", ListingPeriod.Night), ("week", ListingPeriod.Week), ("hour", ListingPeriod.Hour), ("year", ListingPeriod.Year));

    private static readonly IReadOnlyDictionary<string, string> UnitFeatures = Aliases(
        ("Кухня", "kitchen"), ("Интернет", "internet"), ("Балкон", "balcony"), ("Холодильник", "refrigerator"),
        ("Кондиционер", "airConditioning"), ("Стиральная машина", "washingMachine"), ("Телевизор", "television"),
        ("Кабельное ТВ", "cableTv"), ("Телефон", "landline"));

    private static readonly IReadOnlyDictionary<string, string> NearbyFeatures = Aliases(
        ("Больница, поликлиника", "medicalFacility"), ("Школа", "school"), ("Детская площадка", "playground"),
        ("Детский сад", "kindergarten"), ("Остановки", "transitStop"), ("Парк, зелёная зона", "park"),
        ("Развлекательные заведения", "entertainment"), ("Рестораны, кафе", "dining"),
        ("Стоянка", "parking"), ("Супермаркет, магазины", "shops"));

    private static readonly IReadOnlyDictionary<string, string> UtilityFeatures = Aliases(
        ("Газ", "gas"), ("Вода", "water"), ("Электричество", "electricity"), ("Отопление", "heating"),
        ("Канализация", "sewer"));
}
