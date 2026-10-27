namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Refers to the route class a source is read through, recorded on every capture; it gates nothing.</summary>
public enum SourcePosture
{
    /// <summary>The platform's own API under keys Harvest holds.</summary>
    Official,

    /// <summary>A licensed data provider's run.</summary>
    LicensedProvider,

    /// <summary>Harvest's own fetch of public pages.</summary>
    OwnFetch,

    /// <summary>Submissions their senders consented to.</summary>
    ConsentedIntake,
}
