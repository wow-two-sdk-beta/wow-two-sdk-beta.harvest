using System.Text.Json;
using System.Text.Json.Nodes;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Tells whether a post's revision differs from the one a subscription last received only by what Layer 1 and the
/// listing answer put on it, so a filter that asks to may withhold the <c>post.changed</c> it would raise.</summary>
/// <remarks>The revision is tag-only when its <c>base</c>, once <c>tags</c>, <c>classification</c>, <c>extras.postTags</c>
/// and <c>extras.listingAi</c> are set aside on both sides, equals the received one. Anything else that moved, such as the
/// title, the text, a price, a date or the media, makes it a content change that goes out.</remarks>
internal static class TagOnlyChange
{
    private const string TagsName = "tags";
    private const string ClassificationName = "classification";
    private const string ExtrasName = "extras";

    /// <summary>Compares a received <c>base</c> with the post's current one.</summary>
    /// <param name="receivedBase">The <c>base</c> json of the subscription's newest event of the post.</param>
    /// <param name="current">The post's current envelope.</param>
    internal static bool Is(string receivedBase, PostEnvelope current)
    {
        try
        {
            var before = Core(JsonNode.Parse(receivedBase));
            var after = Core(JsonNode.Parse(StoreJson.Write(current)));
            return before is not null && after is not null && JsonNode.DeepEquals(before, after);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonNode? Core(JsonNode? node)
    {
        if (node is not JsonObject post)
            return null;
        post.Remove(TagsName);
        post.Remove(ClassificationName);
        if (post[ExtrasName] is JsonObject extras)
        {
            extras.Remove("postTags");
            extras.Remove("listingAi");
            if (extras.Count == 0)
                post.Remove(ExtrasName);
        }
        return post;
    }
}
