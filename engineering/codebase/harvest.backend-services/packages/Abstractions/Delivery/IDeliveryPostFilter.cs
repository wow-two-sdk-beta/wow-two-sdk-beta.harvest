using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Selects which captured posts enter subscriptions that request a host-registered filter.</summary>
public interface IDeliveryPostFilter
{
    /// <summary>Gets the stable, versioned key persisted by subscriptions.</summary>
    string Key { get; }

    /// <summary>Returns whether the captured envelope matches. Failures withhold delivery without discarding the capture.</summary>
    bool Matches(PostEnvelope envelope);
}
