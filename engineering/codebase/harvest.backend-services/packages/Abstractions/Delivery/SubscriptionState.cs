namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Refers to whether a subscription accumulates events.</summary>
public enum SubscriptionState
{
    /// <summary>Events accumulate and deliver.</summary>
    Active,

    /// <summary>Events accumulate; pushes wait until it resumes.</summary>
    Suspended,

    /// <summary>Closed; no new events.</summary>
    Archived,
}
