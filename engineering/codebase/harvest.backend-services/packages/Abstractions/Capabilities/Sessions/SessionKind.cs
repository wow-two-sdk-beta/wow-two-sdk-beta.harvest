namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Sessions;

/// <summary>Refers to whose session a request carries.</summary>
public enum SessionKind
{
    /// <summary>No cookies at all.</summary>
    None,

    /// <summary>An anonymous cookie jar: cheapest captcha avoidance.</summary>
    Anonymous,

    /// <summary>An account the operator runs: a site login, a platform session.</summary>
    Account,

    /// <summary>An account a consumer owns, valid only in that consumer's private container.</summary>
    Connection,
}
