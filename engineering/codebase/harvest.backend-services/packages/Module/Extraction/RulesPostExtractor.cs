using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;

/// <summary>Extracts source fields and text rules into the base envelope without a model or an external integration.</summary>
public sealed class RulesPostExtractor : IPostExtractor
{
    /// <inheritdoc />
    public Task<Result<PostEnvelope>> ExtractAsync(Capture capture, ExtractionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(request);
        var schemaOrg = capture.Structured.FirstOrDefault(ReaderSupport.IsEvent);
        var metadata = capture.Structured.FirstOrDefault(node => ReaderSupport.String(node, "reader") is "page" or "feed");
        var hasEvent = schemaOrg.ValueKind == JsonValueKind.Object;
        var hasMetadata = metadata.ValueKind == JsonValueKind.Object;
        var title = hasEvent ? ReaderSupport.String(schemaOrg, "name") : hasMetadata ? ReaderSupport.String(metadata, "title") : null;
        var text = hasEvent ? ReaderSupport.String(schemaOrg, "description") : hasMetadata ? ReaderSupport.String(metadata, "text") : capture.ContentProjection;
        title = NullText(title);
        text = NullText(text, preserveLineBreaks: true);
        var fieldSources = new Dictionary<string, FieldSource>();
        var parser = hasEvent ? "schema.org.event" : hasMetadata ? ReaderSupport.String(metadata, "reader") : "capture";
        if (title is not null) fieldSources["title"] = Deterministic(parser);
        if (text is not null) fieldSources["text"] = Deterministic(parser);
        var allText = (title ?? string.Empty) + " " + (text ?? string.Empty);
        var dates = new List<PostDate>();
        var prices = new List<PostPrice>();
        PostLocation? location = null;
        PostAuthor? author = null;
        var media = capture.Media.Select(item => new PostMedia { Kind = item.Kind, Url = item.Url }).ToList();
        var links = capture.Links.Select(item => new PostLink { Url = item.Url, Text = item.Text, Kind = PostLinkKind.Site }).ToList();
        var extras = new Dictionary<string, JsonElement>();
        if (hasMetadata && metadata.TryGetProperty("sourceFields", out JsonElement sourceFields)
            && sourceFields.ValueKind == JsonValueKind.Object)
            extras["source"] = sourceFields.Clone();
        string[] sourceTags = hasMetadata && metadata.TryGetProperty("sourceTags", out JsonElement tagNodes)
            && tagNodes.ValueKind == JsonValueKind.Array
            ? tagNodes.EnumerateArray().Where(tag => tag.ValueKind == JsonValueKind.String)
                .Select(tag => tag.GetString()!).Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.Ordinal).ToArray() : [];
        if (sourceTags.Length > 0) fieldSources["sourceTags"] = Deterministic(parser);
        if (hasEvent)
        {
            extras["schemaOrg"] = schemaOrg.Clone();
            if (ExtractionRules.Date(ReaderSupport.String(schemaOrg, "startDate"), DateRole.Start) is { } start) dates.Add(start);
            if (ExtractionRules.Date(ReaderSupport.String(schemaOrg, "endDate"), DateRole.End) is { } end) dates.Add(end);
            if (dates.Count > 0) fieldSources["dates"] = Deterministic("schema.org.event");
            if (schemaOrg.TryGetProperty("location", out var place)) location = Location(place);
            if (location is not null) fieldSources["location"] = Deterministic("schema.org.event");
            if (schemaOrg.TryGetProperty("organizer", out var organizer))
                author = new PostAuthor
                {
                    Name = ReaderSupport.String(organizer, "name") ?? ReaderSupport.Scalar(organizer),
                    Url = ReaderSupport.Url(new Uri(capture.Url), ReaderSupport.String(organizer, "url")),
                };
            if (schemaOrg.TryGetProperty("offers", out var offers))
            {
                foreach (var offer in offers.ValueKind == JsonValueKind.Array ? offers.EnumerateArray().ToArray() : [offers])
                {
                    var raw = ReaderSupport.String(offer, "price");
                    if (ExtractionRules.Amount(raw, out var amount))
                        prices.Add(new() { Amount = amount, Currency = ReaderSupport.String(offer, "priceCurrency"), Free = amount == 0, Raw = raw, Role = PriceRole.Ticket });
                    else if (ExtractionRules.Amount(ReaderSupport.String(offer, "lowPrice"), out var low))
                    {
                        var hasHigh = ExtractionRules.Amount(ReaderSupport.String(offer, "highPrice"), out var high);
                        prices.Add(new() { Min = low, Max = hasHigh ? high : null, Currency = ReaderSupport.String(offer, "priceCurrency"), Role = PriceRole.Ticket });
                    }
                    if (ReaderSupport.Url(new Uri(capture.Url), ReaderSupport.String(offer, "url")) is { } offerUrl)
                        links.Add(new() { Url = offerUrl, Kind = PostLinkKind.Ticket });
                }
                if (prices.Count > 0) fieldSources["prices"] = Deterministic("schema.org.event");
            }
            if (schemaOrg.TryGetProperty("image", out var images))
            {
                foreach (var image in images.ValueKind == JsonValueKind.Array ? images.EnumerateArray().ToArray() : [images])
                {
                    var raw = ReaderSupport.Scalar(image) ?? ReaderSupport.String(image, "url") ?? ReaderSupport.String(image, "contentUrl");
                    if (ReaderSupport.Url(new Uri(capture.Url), raw) is { } imageUrl)
                        media.Add(new() { Kind = MediaKind.Image, Url = imageUrl });
                }
            }
        }
        else if (hasMetadata)
        {
            extras[ReaderSupport.String(metadata, "reader") == "feed" ? "feed" : "openGraph"] = metadata.Clone();
            if (ReaderSupport.String(metadata, "author") is { Length: > 0 } name) author = new() { Name = name };
        }
        if (author is not null) fieldSources["author"] = Deterministic(parser);
        var ruleDates = ExtractionRules.Dates(allText).Where(date => dates.All(existing => existing.Value != date.Value)).ToList();
        dates.AddRange(ruleDates);
        if (ruleDates.Count > 0 && !fieldSources.ContainsKey("dates")) fieldSources["dates"] = Rules("iso-and-dotted-dates@1");
        if (prices.Count == 0)
        {
            prices.AddRange(ExtractionRules.Prices(allText));
            if (prices.Count > 0) fieldSources["prices"] = Rules("published-price@1");
        }
        var locale = capture.Locale ?? (hasEvent ? ReaderSupport.String(schemaOrg, "inLanguage") : null);
        var language = ExtractionRules.Language(allText, locale);
        if (language.Code != "und") fieldSources["language"] = locale is null ? Rules("language-script@1") : Deterministic(parser);
        var uzbek = new Uri(capture.Url).IdnHost.EndsWith(".uz", StringComparison.OrdinalIgnoreCase) || locale?.StartsWith("uz", StringComparison.OrdinalIgnoreCase) == true;
        var contactText = allText;
        if (hasMetadata && metadata.TryGetProperty("contacts", out var contactNodes) && contactNodes.ValueKind == JsonValueKind.Array)
            contactText += " " + string.Join(" ", contactNodes.EnumerateArray().Select(ReaderSupport.Scalar));
        var contacts = ExtractionRules.Contacts(contactText, uzbek);
        if (contacts.Count > 0) fieldSources["contacts"] = Rules("e164-and-email@2");
        var postedAt = capture.PublishedAtUtc is { } published
            ? new PostTimestamp { Value = published.ToString("O"), Precision = DatePrecision.Second, TimeZone = "UTC" } : null;
        if (postedAt is not null) fieldSources["postedAt"] = Deterministic(parser);
        if (media.Count > 0) fieldSources["media"] = Deterministic(parser);
        if (links.Count > 0) fieldSources["links"] = Deterministic(parser);
        var envelope = new PostEnvelope
        {
            Kind = hasEvent ? PostKind.Event : request.DefaultKind,
            Title = title, Text = text, Author = author, Language = language, PostedAt = postedAt,
            Dates = dates, Prices = prices, Location = location, Contacts = contacts, SourceTags = sourceTags,
            Media = media.DistinctBy(item => item.Url).ToList(), Links = links.DistinctBy(item => item.Url).ToList(),
            Extras = extras, FieldSources = fieldSources,
            Flags = capture.Warnings.Select(warning => "source_warning:" + warning)
                .Concat(request.Ai != AiMode.Off ? ["ai_skipped:capability_unavailable"] : Array.Empty<string>())
                .Distinct(StringComparer.Ordinal).ToArray(),
            Extraction = new() { NormVersion = "norm@1", RulesVersion = "rules@2", TiersRun = [ExtractionTier.Deterministic, ExtractionTier.Rules] },
        };
        if (EventSchemaMapper.Map(envelope) is { } mapped) extras["event"] = mapped;
        return Task.FromResult(Result<PostEnvelope>.Ok(envelope));
    }

    private static string? NullText(string? value, bool preserveLineBreaks = false)
    {
        string text = preserveLineBreaks && value is not null
            ? string.Join('\n', value.Split('\n').Select(ReaderSupport.HtmlText).Where(line => line.Length > 0))
            : ReaderSupport.HtmlText(value);
        return text.Length == 0 ? null : text;
    }

    private static FieldSource Deterministic(string? parser) => new() { Tier = ExtractionTier.Deterministic, Rule = parser };
    private static FieldSource Rules(string rule) => new() { Tier = ExtractionTier.Rules, Rule = rule };

    private static PostLocation? Location(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().Select(Location).FirstOrDefault(location => location is not null);
        var mentions = new List<LocationMention>();
        if (ReaderSupport.Scalar(value) is { Length: > 0 } text) mentions.Add(new() { Kind = LocationKind.Venue, Text = text });
        if (ReaderSupport.String(value, "name") is { Length: > 0 } name) mentions.Add(new() { Kind = LocationKind.Venue, Text = name });
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("address", out var address))
        {
            if (ReaderSupport.Scalar(address) is { Length: > 0 } raw) mentions.Add(new() { Kind = LocationKind.Address, Text = raw });
            foreach (var (property, kind) in new[] { ("addressLocality", LocationKind.City), ("addressRegion", LocationKind.Region), ("streetAddress", LocationKind.Street) })
                if (ReaderSupport.String(address, property) is { Length: > 0 } part) mentions.Add(new() { Kind = kind, Text = part });
        }
        GeoPoint? geo = null;
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("geo", out var coordinates)
            && double.TryParse(ReaderSupport.String(coordinates, "latitude"), System.Globalization.CultureInfo.InvariantCulture, out var latitude)
            && double.TryParse(ReaderSupport.String(coordinates, "longitude"), System.Globalization.CultureInfo.InvariantCulture, out var longitude)
            && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180)
            geo = new() { Latitude = latitude, Longitude = longitude };
        return mentions.Count > 0 || geo is not null ? new() { Mentions = mentions, Geo = geo } : null;
    }
}
