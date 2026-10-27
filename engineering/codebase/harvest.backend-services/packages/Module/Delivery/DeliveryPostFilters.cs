using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Resolves a subscription's filter without allowing unavailable or failed filters to admit posts.</summary>
internal sealed class DeliveryPostFilters(IEnumerable<IDeliveryPostFilter> filters)
{
    private readonly Dictionary<string, IDeliveryPostFilter> byKey = filters.ToDictionary(filter => filter.Key, StringComparer.Ordinal);

    internal bool Contains(string key) => byKey.ContainsKey(key);

    internal bool Matches(string? key, PostEnvelope envelope)
    {
        if (key is null) return true;
        if (!byKey.TryGetValue(key, out var filter)) return false;
        try { return filter.Matches(envelope); }
        catch (Exception) { return false; }
    }

    internal static bool IsLifecycle(string type) => type is DeliveryEventTypes.LinkState or DeliveryEventTypes.PostMerged
        or DeliveryEventTypes.PostUnmerged or DeliveryEventTypes.PostPurged or DeliveryEventTypes.PostRelisted;
}
