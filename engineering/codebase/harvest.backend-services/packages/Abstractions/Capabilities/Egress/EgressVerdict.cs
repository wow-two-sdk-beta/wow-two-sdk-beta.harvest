namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Egress;

/// <summary>Refers to how a host treated an exit, for the health ledger per exit and host.</summary>
public enum EgressVerdict
{
    /// <summary>The host answered normally.</summary>
    Ok,

    /// <summary>The host blocked the exit (an IP-scoped block): retry on a fresh exit before counting it.</summary>
    Blocked,

    /// <summary>The host challenged the exit.</summary>
    Challenged,

    /// <summary>The proxy refused the credentials.</summary>
    ProxyAuthFailed,

    /// <summary>The proxy or the exit did not answer.</summary>
    Unreachable,
}
