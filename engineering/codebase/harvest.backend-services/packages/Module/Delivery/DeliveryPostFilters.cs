using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Resolves a subscription's filter without allowing unavailable or failed filters to admit posts.</summary>
internal sealed class DeliveryPostFilters(IEnumerable<IDeliveryPostFilter> filters)
{
    private readonly Dictionary<string, IDeliveryPostFilter> byKey = filters.ToDictionary(filter => filter.Key, StringComparer.Ordinal);

    internal bool Contains(string key) => byKey.ContainsKey(key) || DeliveryTagFilter.IsValid(key);

    internal bool Matches(string? key, PostEnvelope envelope) => Evaluate(key, envelope) == true;

    /// <summary>Tells whether the post passes, fails, or cannot be judged because the filter is absent or threw.</summary>
    internal bool? Evaluate(string? key, PostEnvelope envelope)
    {
        if (key is null) return true;
        if (!byKey.TryGetValue(key, out var filter)) return DeliveryTagFilter.IsValid(key) ? DeliveryTagFilter.Matches(key, envelope) : null;
        try { return filter.Matches(envelope); }
        catch (Exception) { return null; }
    }

    /// <summary>Tells whether the filter withholds the <c>post.changed</c> of a revision that only moved the post's tags.</summary>
    internal bool IgnoresTagOnlyChange(string? key) => key is not null && byKey.TryGetValue(key, out var filter) && filter.IgnoresTagOnlyChange;

    internal static bool IsLifecycle(string type) => type is DeliveryEventTypes.LinkState or DeliveryEventTypes.PostMerged
        or DeliveryEventTypes.PostUnmerged or DeliveryEventTypes.PostPurged or DeliveryEventTypes.PostRelisted
        or DeliveryEventTypes.PostUnmatched;
}
