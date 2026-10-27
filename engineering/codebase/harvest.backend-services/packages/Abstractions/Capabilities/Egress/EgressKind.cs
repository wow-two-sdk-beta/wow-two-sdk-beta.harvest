namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Egress;

/// <summary>Refers to where a request leaves for the internet.</summary>
public enum EgressKind
{
    /// <summary>The host's own address, through the safe outbound client.</summary>
    Direct,

    /// <summary>A datacenter proxy.</summary>
    Datacenter,

    /// <summary>A residential proxy.</summary>
    Residential,

    /// <summary>A mobile-network proxy.</summary>
    Mobile,

    /// <summary>A fixed exit the operator runs (an IP server), for identity or geography.</summary>
    Static,

    /// <summary>A vendor unlocker that adds egress, fingerprint, rendering and solving.</summary>
    Unlocker,
}
