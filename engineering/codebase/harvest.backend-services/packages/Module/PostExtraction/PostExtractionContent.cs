using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Represents the done <c>post-tags</c> answer an extraction reads: the one for the post's current title and text.</summary>
internal sealed record ExtractionTags(string EntityType, IReadOnlyList<PostTagsDomain> Domains, string? About, string? Timeframe, string? TitleEn)
{
    /// <summary>Gets the primary node, the first when none is marked.</summary>
    internal string? Primary => Domains.FirstOrDefault(domain => domain.Primary)?.Node ?? Domains.FirstOrDefault()?.Node;
}

/// <summary>Represents what quotes are checked against: the title and the whole text, phone-scrubbed as the prompt received
/// them, of which the model read the first <see cref="Visible"/> characters, and the rendered structured variables the prompt
/// received (<see cref="Prices"/>, <see cref="Dates"/>, <see cref="Place"/>, and for a listing <see cref="Attributes"/>, the
/// <c>label: value</c> lines; <c>null</c> when the source gave none). A single captured asking price can back a listing price.</summary>
internal sealed record ExtractionCorpus(string Text, int Visible, string? Prices, string? Dates, string? Place,
    string? Attributes = null, PostPrice? ListedPrice = null)
{
    /// <summary>Initializes a corpus with no structured variables.</summary>
    internal ExtractionCorpus(string text, int visible) : this(text, visible, null, null, null, null)
    {
    }

    /// <summary>Gets the one text a rental-term check reads, so the terms of one answer may quote the title and text or the attribute
    /// lines: what the model read of the title and text, a separator no quote holds, then the attribute lines. A quote that
    /// spans the two does not appear in it as written; its length is the visible part.</summary>
    internal (string Text, int Visible) TermSource()
    {
        if (Attributes is null)
            return (Text, Visible);
        string combined = $"{Text[..Math.Min(Visible, Text.Length)]}{AttributesSeparator}{Attributes}";
        return (combined, combined.Length);
    }

    private const string AttributesSeparator = "\n\u001e\n";

    /// <summary>Gets the structured renders that exist, each a separate text a quote may come from.</summary>
    internal IEnumerable<string> Structured => new[] { Prices, Dates, Place, Attributes }.OfType<string>();
}

/// <summary>Turns a post and its tags into what the extraction prompt receives and what its quotes must come from.</summary>
internal static partial class PostExtractionContent
{
    /// <summary>The source-fields schema of an OLX ad, whose attributes and seller category the listing prompt reads.</summary>
    internal const string OlxSchema = "olx.ad.v1";

    /// <summary>The source-fields schema of a Joymee ad, whose attributes and account type the listing prompt reads.</summary>
    internal const string JoymeeSchema = "joymee.ad.v1";

    private const string HataSchema = "hata.ad.v1";

    private const int MaxPrices = 10;
    private const int MaxAttributes = 40;
    private const int MaxLine = 200;

    /// <summary>Builds the quote corpus: the title, a line break and the text, both phone-scrubbed as the prompt gets them, of which
    /// the model reads the title and the text cut to <paramref name="maxTextChars"/>, plus the <c>prices</c>, <c>dates</c> and <c>place</c> renders the prompt received,
    /// word for word: the dates leave out an estimated one, and a render that reads <c>none</c> or <c>unknown</c> is no source.</summary>
    internal static ExtractionCorpus Corpus(PostEnvelope envelope, int maxTextChars)
    {
        string title = PostTagsContent.Title(envelope);
        string text = PhoneScrub.Apply(envelope.Text) ?? "";
        string cut = PostTagsContent.Cut(text, maxTextChars);
        var listed = envelope.Prices is [{ Role: PriceRole.Listed, Amount: > 0, Free: false, Min: null, Max: null } price]
            ? price : null;
        return new($"{title}\n{text}", title.Length + 1 + cut.Length, Render(Prices(envelope.Prices), "none"),
            Render(PostTagsContent.Dates(envelope.Dates), "none"), Render(PostTagsContent.Place(envelope.Location), "unknown"),
            Render(Attributes(envelope), ""), listed);
    }

    /// <summary>Builds the prompt variables: the post fields <c>title</c>, <c>text</c> (phone-scrubbed, then cut), <c>sourceCategory</c>,
    /// <c>language</c>, <c>dates</c>, <c>place</c>, <c>prices</c>, <c>postedAt</c>, and the tags <c>entityType</c>,
    /// <c>domains</c>, <c>about</c>, <c>timeframe</c>, <c>titleEn</c>; an absent value reads <c>unknown</c> or <c>none</c>.
    /// <paramref name="firstSeen"/> bounds an unknown publication date.</summary>
    internal static Dictionary<string, string?> Variables(PostEnvelope envelope, string? sourceCategory, ExtractionTags tags, int maxTextChars,
        DateTimeOffset? firstSeen = null) => new()
    {
        ["title"] = PostTagsContent.Title(envelope),
        ["text"] = PostTagsContent.Text(envelope, maxTextChars),
        ["sourceCategory"] = string.IsNullOrWhiteSpace(sourceCategory) ? "unknown" : sourceCategory.Trim(),
        ["language"] = envelope.Language?.Code ?? "und",
        ["dates"] = PostTagsContent.Dates(envelope.Dates),
        ["place"] = PostTagsContent.Place(envelope.Location),
        ["prices"] = Prices(envelope.Prices),
        ["postedAt"] = PostTagsContent.PostedAt(envelope.PostedAt, firstSeen),
        ["entityType"] = tags.EntityType,
        ["domains"] = tags.Domains.Count == 0 ? "none"
            : string.Join(", ", tags.Domains.OrderByDescending(domain => domain.Primary).Select(domain => domain.Node)),
        ["about"] = string.IsNullOrWhiteSpace(tags.About) ? "unknown" : tags.About,
        ["timeframe"] = tags.Timeframe ?? "unknown",
        ["titleEn"] = string.IsNullOrWhiteSpace(tags.TitleEn) ? "unknown" : tags.TitleEn,
    };

    /// <summary>Builds the prompt variables for one use case, including allowed event formats or listing details.
    /// An eval subject resolves exactly what the pass sends.</summary>
    /// <param name="useCase"><c>extract-event</c> or <c>extract-listing</c>.</param>
    /// <param name="envelope">The post's current fields.</param>
    /// <param name="sourceCategory">The category hint: the source tags, else the section.</param>
    /// <param name="tags">The done tags for the post's current title and text.</param>
    /// <param name="maxTextChars">How much of the text the prompt receives.</param>
    /// <param name="firstSeen">The post's first capture, which bounds an unknown publication date.</param>
    /// <param name="section">The harvest section the post came from: its catalog key, else its name; none reads empty.</param>
    internal static Dictionary<string, string?> VariablesFor(string useCase, PostEnvelope envelope, string? sourceCategory, ExtractionTags tags,
        int maxTextChars, DateTimeOffset? firstSeen = null, string? section = null)
    {
        var variables = Variables(envelope, sourceCategory, tags, maxTextChars, firstSeen);
        if (useCase == PostExtractionConstants.EventUseCase)
            variables["eventFormats"] = PostExtractionDefinitions.EventFormatDefinitions;
        else if (useCase == PostExtractionConstants.ListingUseCase)
        {
            variables["attributes"] = Attributes(envelope);
            variables["section"] = Section(section);
            variables["seller"] = Seller(envelope);
        }
        return variables;
    }

    /// <summary>Renders the post's own listing attributes, one <c>label: value</c> line each in the source's order and language,
    /// phone-scrubbed. The OLX, Joymee and HATA adapters carry these attributes.</summary>
    internal static string Attributes(PostEnvelope envelope)
    {
        if (AdFields(envelope, OlxSchema, JoymeeSchema, HataSchema) is not { } fields
            || !fields.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
            return "";
        var lines = new List<string>();
        foreach (var attribute in attributes.EnumerateObject())
        {
            if (lines.Count >= MaxAttributes)
                break;
            string label = Line(attribute.Name), value = Line(attribute.Value.ValueKind == JsonValueKind.String ? attribute.Value.GetString() : null);
            if (label.Length == 0 && value.Length == 0)
                continue;
            lines.Add(label.Length == 0 ? value : value.Length == 0 || value == label ? label : $"{label}: {value}");
        }
        return PhoneScrub.Apply(string.Join('\n', lines)) ?? "";
    }

    /// <summary>Reads the site's own account type of the seller. An OLX ad (<c>olx.ad.v1</c>) reads <c>business</c> or <c>private</c> from
    /// the ad's <c>isBusiness</c> flag, else from the seller badge among its attributes (<c>Бизнес</c>, <c>Частное лицо</c>,
    /// <c>Biznes</c>, <c>Jismoniy shaxs</c>), else <c>unknown</c>. A Joymee ad (<c>joymee.ad.v1</c>) reads <c>owner</c> for its
    /// <c>advertiserType</c> 1, <c>agent</c> for 2, else <c>unknown</c>. Any other source reads empty. It names no person: the seller's
    /// name and registration date never reach a prompt.</summary>
    internal static string Seller(PostEnvelope envelope)
    {
        if (AdFields(envelope, JoymeeSchema) is { } joymee)
            return joymee.TryGetProperty("advertiserType", out var type) ? AccountType(type) : "unknown";
        if (AdFields(envelope, OlxSchema) is not { } fields)
            return "";
        if (fields.TryGetProperty("isBusiness", out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return flag.ValueKind == JsonValueKind.True ? "business" : "private";
        if (fields.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object)
            foreach (var attribute in attributes.EnumerateObject())
                foreach (string text in new[] { attribute.Name, attribute.Value.ValueKind == JsonValueKind.String ? attribute.Value.GetString() ?? "" : "" })
                    switch (Badge(text))
                    {
                        case "business":
                            return "business";
                        case "private":
                            return "private";
                    }
        return "unknown";
    }

    /// <summary>Renders the harvest section the post came from, or empty when none is known.</summary>
    internal static string Section(string? section) => Line(section);

    private static string Badge(string text) => Line(text).ToLowerInvariant().Replace('ё', 'е') switch
    {
        "бизнес" or "biznes" or "business" => "business",
        "частное лицо" or "jismoniy shaxs" or "private" => "private",
        _ => "",
    };

    private static string AccountType(JsonElement type) => (type.ValueKind == JsonValueKind.String ? type.GetString()?.Trim() : type.ToString()) switch
    {
        "1" => "owner",
        "2" => "agent",
        _ => "unknown",
    };

    private static JsonElement? AdFields(PostEnvelope envelope, params string[] schemas) =>
        envelope.Extras.TryGetValue("source", out var source) && source.ValueKind == JsonValueKind.Object
        && source.TryGetProperty("schema", out var schema) && schema.ValueKind == JsonValueKind.String && schemas.Contains(schema.GetString())
        && source.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object ? fields : null;

    private static string Line(string? text) => text is null ? ""
        : PostTagsContent.Cut(Whitespace().Replace(text, " ").Trim(), MaxLine);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    /// <summary>Renders the prices a reader mapped, one <c>role: amount[–max] currency[/per]</c> per price, or <c>none</c>.</summary>
    internal static string Prices(IReadOnlyList<PostPrice> prices) => prices.Count == 0 ? "none"
        : string.Join("; ", prices.Take(MaxPrices).Select(price =>
        {
            string amount = price.Free ? "free"
                : price.Amount is { } value ? Number(value)
                : price.Min is { } min ? $"{Number(min)}–{(price.Max is { } max ? Number(max) : "")}"
                : price.Raw?.Trim() ?? "unknown";
            string currency = string.IsNullOrWhiteSpace(price.Currency) ? "" : " " + price.Currency.Trim();
            string per = string.IsNullOrWhiteSpace(price.Per) ? "" : "/" + price.Per.Trim();
            return $"{price.Role.ToString().ToLowerInvariant()}: {amount}{currency}{per}";
        }));

    private static string? Render(string render, string absent) => render == absent ? null : render;

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
