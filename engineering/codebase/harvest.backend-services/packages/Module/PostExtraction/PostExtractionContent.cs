using System.Globalization;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Represents the done <c>post-tags</c> answer an extraction reads: the one for the post's current title and text.</summary>
internal sealed record ExtractionTags(string EntityType, IReadOnlyList<PostTagsDomain> Domains, string? About, string? Timeframe, string? TitleEn)
{
    /// <summary>Gets the primary node, the first when none is marked.</summary>
    internal string? Primary => Domains.FirstOrDefault(domain => domain.Primary)?.Node ?? Domains.FirstOrDefault()?.Node;
}

/// <summary>Represents the text quotes are checked against: the title and the whole text, of which the model read the
/// first <see cref="Visible"/> characters.</summary>
internal sealed record ExtractionCorpus(string Text, int Visible);

/// <summary>Turns a post and its tags into what the extraction prompt receives and what its quotes must come from.</summary>
internal static class PostExtractionContent
{
    private const int MaxPrices = 10;

    /// <summary>Builds the quote corpus: the title, a line break and the text; the model reads the title and the text cut
    /// to <paramref name="maxTextChars"/>.</summary>
    internal static ExtractionCorpus Corpus(PostEnvelope envelope, int maxTextChars)
    {
        string title = envelope.Title?.Trim() ?? "";
        string text = envelope.Text ?? "";
        string cut = PostTagsContent.Cut(text, maxTextChars);
        return new($"{title}\n{text}", title.Length + 1 + cut.Length);
    }

    /// <summary>Builds the prompt variables: the post fields <c>title</c>, <c>text</c> (cut), <c>sourceCategory</c>,
    /// <c>language</c>, <c>dates</c>, <c>place</c>, <c>prices</c>, <c>postedAt</c>, and the tags <c>entityType</c>,
    /// <c>domains</c>, <c>about</c>, <c>timeframe</c>, <c>titleEn</c>; an absent value reads <c>unknown</c> or <c>none</c>.</summary>
    internal static Dictionary<string, string?> Variables(PostEnvelope envelope, string? sourceCategory, ExtractionTags tags, int maxTextChars) => new()
    {
        ["title"] = envelope.Title?.Trim() ?? "",
        ["text"] = PostTagsContent.Cut(envelope.Text, maxTextChars),
        ["sourceCategory"] = string.IsNullOrWhiteSpace(sourceCategory) ? "unknown" : sourceCategory.Trim(),
        ["language"] = envelope.Language?.Code ?? "und",
        ["dates"] = PostTagsContent.Dates(envelope.Dates),
        ["place"] = PostTagsContent.Place(envelope.Location),
        ["prices"] = Prices(envelope.Prices),
        ["postedAt"] = PostTagsContent.PostedAt(envelope.PostedAt),
        ["entityType"] = tags.EntityType,
        ["domains"] = tags.Domains.Count == 0 ? "none"
            : string.Join(", ", tags.Domains.OrderByDescending(domain => domain.Primary).Select(domain => domain.Node)),
        ["about"] = string.IsNullOrWhiteSpace(tags.About) ? "unknown" : tags.About,
        ["timeframe"] = tags.Timeframe ?? "unknown",
        ["titleEn"] = string.IsNullOrWhiteSpace(tags.TitleEn) ? "unknown" : tags.TitleEn,
    };

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

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
