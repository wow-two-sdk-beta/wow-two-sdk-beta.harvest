using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Builds <c>extras.listingAi</c>, what a done <c>extract-listing</c> answer carries to a subscriber inside its
/// <c>post.extracted</c> event.</summary>
/// <remarks>
///   - <c>schema</c> <c>extract-listing@2</c>, <c>promptVersion</c> and <c>model</c> (<c>{provider}/{model}</c>) of the answer, the
///     <c>costMicroUsd</c> of the run that gave it (zero for a cached answer), <c>revision</c> (the post revision the answer read),
///     the checked <c>answer</c> and its <c>flags</c>
///   - the answer has no field for a name, phone or contact, so none travels; the post's own stored fields are not touched
/// </remarks>
internal static class ListingAiExtras
{
    /// <summary>The key of the extras namespace on the delivered post's <c>base</c>.</summary>
    internal const string Key = "listingAi";

    /// <summary>The answer's schema, as the event names it.</summary>
    internal const string Schema = "extract-listing@2";

    /// <summary>Builds the extras value of a done answer.</summary>
    /// <param name="record">The stored answer, which must be done and carry its answer.</param>
    internal static JsonElement Build(PostExtractionRecord record) => JsonSerializer.SerializeToElement(new Dictionary<string, object?>
    {
        ["schema"] = Schema,
        ["promptVersion"] = record.PromptVersion,
        ["model"] = record.Model,
        ["costMicroUsd"] = record.CostMicroUsd,
        ["revision"] = record.Revision,
        ["answer"] = record.Answer!.Answer,
        ["flags"] = record.Answer.Flags,
    }, JsonOptionsConstants.Default);

    /// <summary>Names the answer within its post revision, so a re-run by another prompt version or model is its own event and a
    /// replay of the same one is not: <c>:ai{promptVersion}:{model}</c>.</summary>
    /// <param name="record">The stored answer.</param>
    internal static string ItemSuffix(PostExtractionRecord record) => $":ai{record.PromptVersion}:{record.Model}";
}
