namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Refers to how a subscription's event log reaches its tenant.</summary>
public enum DeliveryMode
{
    /// <summary>Signed webhook pushes.</summary>
    Webhook,

    /// <summary>A pull cursor.</summary>
    Pull,

    /// <summary>Both doors over the one log.</summary>
    Both,
}
