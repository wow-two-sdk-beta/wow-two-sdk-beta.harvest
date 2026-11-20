using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Selects which captured posts enter subscriptions that request a host-registered filter.</summary>
public interface IDeliveryPostFilter
{
    /// <summary>Gets the stable, versioned key persisted by subscriptions.</summary>
    string Key { get; }

    /// <summary>Returns whether the captured envelope matches. Failures withhold delivery without discarding the capture.</summary>
    bool Matches(PostEnvelope envelope);

    /// <summary>Gets whether a subscription of this filter goes without the <c>post.changed</c> of a revision that differs from
    /// the one it last received only in the post's tags, its classification and the AI answers carried in its extras: a
    /// content change still goes out, and so does a state change. The default is false: every revision goes out.</summary>
    bool IgnoresTagOnlyChange => false;
}
