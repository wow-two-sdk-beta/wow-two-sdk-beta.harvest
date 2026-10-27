namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Sessions;

/// <summary>Refers to the state a session is released in.</summary>
public enum SessionOutcome
{
    /// <summary>The session still works; a host-scoped jar is kept warm.</summary>
    Ok,

    /// <summary>The host no longer accepts the session; it is dropped.</summary>
    Expired,

    /// <summary>The host asked to sign in again; an account session needs a person.</summary>
    LoginRequired,
}
