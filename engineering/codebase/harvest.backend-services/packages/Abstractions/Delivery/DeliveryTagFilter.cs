using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Applies a literal post tag requested by a pull subscription.</summary>
public static class DeliveryTagFilter
{
    private const string Prefix = "tag:";

    /// <summary>Tells whether the key requests a non-empty post tag.</summary>
    public static bool IsValid(string key) => key.StartsWith(Prefix, StringComparison.Ordinal)
        && key.Length <= 200 && !string.IsNullOrWhiteSpace(key[Prefix.Length..]);

    /// <summary>Matches an exact normalized or source tag, ignoring case.</summary>
    public static bool Matches(string key, PostEnvelope post)
    {
        if (!IsValid(key)) return false;
        string tag = key[Prefix.Length..].Trim();
        return post.Tags?.Any(value => string.Equals(value?.Tag, tag, StringComparison.OrdinalIgnoreCase)) == true
            || post.SourceTags?.Any(value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase)) == true;
    }
}
