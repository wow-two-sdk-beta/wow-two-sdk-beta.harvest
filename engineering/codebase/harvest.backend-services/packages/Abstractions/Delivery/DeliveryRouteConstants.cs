namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Holds the public pull delivery routes.</summary>
public static class DeliveryRouteConstants
{
    /// <summary>Holds the subscription collection route.</summary>
    public const string Subscriptions = "api/subscriptions";

    /// <summary>Holds the route that previews a selector over a container's posts.</summary>
    public const string SubscriptionPreview = "api/subscriptions/preview";

    /// <summary>Returns the route that replaces a selector subscription's selector.</summary>
    public static string Selector(Guid subscriptionId) => $"{Subscriptions}/{subscriptionId}/selector";

    /// <summary>Returns the route that changes a subscription's state.</summary>
    public static string State(Guid subscriptionId) => $"{Subscriptions}/{subscriptionId}/state";
}
