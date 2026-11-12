using System.Text.Json;
using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

/// <summary>Selects fashion, garment, textile and artisan opportunities for Ocharo's internal event catalog.</summary>
public sealed partial class OcharoFashionFilter : IDeliveryPostFilter
{
    /// <summary>The stable subscription filter key.</summary>
    public const string FilterKey = "ocharo-fashion-v1";
    /// <inheritdoc />
    public string Key => FilterKey;

    /// <inheritdoc />
    public bool Matches(PostEnvelope envelope)
    {
        if (envelope.Kind != PostKind.Event)
            return false;
        if (envelope.Extras.TryGetValue("schemaOrg", out JsonElement node) && node.ValueKind == JsonValueKind.Object)
        {
            foreach (string source in new[] { "afisha", "anons", "craftfair", "uzexpocentre" })
                if (node.TryGetProperty(source, out JsonElement evidence) && evidence.ValueKind == JsonValueKind.Object
                    && evidence.TryGetProperty("recordKind", out JsonElement kind) && kind.GetString() == "article")
                    return false;
        }
        string text = (envelope.Title ?? string.Empty) + " " + (envelope.Text ?? string.Empty);
        if (TradeAndRetailFamily().IsMatch(envelope.Title ?? string.Empty))
            return true;
        if (!Subject().IsMatch(text))
            return false;
        // Explicit fashion/handmade names are opportunity evidence. General mentions need an event/call context.
        return NamedOpportunity().IsMatch(text) || EventContext().IsMatch(text);
    }

    [GeneratedRegex(@"\b(?:fashion|runway|fashline|texworld|heimtextil|apparel|uzcharm|caitme|textile|garment|hand[-\s]*made|hand[-\s]*craft|hand\s*fest|craft(?!\s+(?:beer|brew))\w*|designer|atlas\s+bayram|silk|embroid|accessor|jewel)|\bмод(?:а|ы|е|у|н\w*)?\b|\bmoda\b|дизайнер|одежд|текстил|ткан[ьи]|ш[её]лк|вышив|рем[её]сл|рукодел|ручн\w*\s+работ|мастериц|аксессуар|украшен|hunarmand|to['‘’ʻ]?qimach|kiyim|ipak|kashta|dizayn|zargar|taqinchoq|qo['‘’ʻ]?l\s+mehnat|ҳунарманд|тўқимач|кийим|ипак|кашта|қўл\s+меҳнат", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Subject();
    [GeneratedRegex(@"\bTITF\b|\bCAEx\s+Baraka\s+Friday\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TradeAndRetailFamily();

    [GeneratedRegex(@"fashion\s*week|fashline|texworld|heimtextil|apparel\s+sourcing|uzcharm|caitme|craft\s*fair|hand\s*fest|atlas\s+bayram", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedOpportunity();
    [GeneratedRegex(@"event|show|week|fair|market|exhibit|expo|festival|competition|contest|conference|application|call\s+for|ярмарк|маркет|выстав|показ|фестивал|конкурс|заявк|конференц|недел[яию]\s+мод|ko['‘’ʻ]?rgazma|yarmarka|bozor|tanlov|ariza|festival|haftali|намойиш|кўргазма|ярмарка|танлов|ариза", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EventContext();
}
