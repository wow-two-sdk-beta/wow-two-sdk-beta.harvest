namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Refers to what a new subscriber's log starts with.</summary>
public enum BackfillMode
{
    /// <summary>A replay of the pipeline's live posts; the default.</summary>
    Live,

    /// <summary>Nothing: new events only.</summary>
    None,

    /// <summary>Posts seen since a date.</summary>
    Since,
}
