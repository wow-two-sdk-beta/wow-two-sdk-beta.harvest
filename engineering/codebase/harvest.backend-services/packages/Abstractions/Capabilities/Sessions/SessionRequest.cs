namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Sessions;

/// <summary>Represents a request for a session lease.</summary>
public sealed record SessionRequest
{
    /// <summary>Gets whose session is wanted.</summary>
    public required SessionKind Kind { get; init; }

    /// <summary>Gets the host the session is for.</summary>
    public required string HostKey { get; init; }

    /// <summary>Gets how long an anonymous session lives.</summary>
    public SessionScope Scope { get; init; } = SessionScope.Run;

    /// <summary>Gets the key a run-scoped session binds to, such as the run's identifier.</summary>
    public string? BoundTo { get; init; }

    /// <summary>Gets the account pool an account session comes from.</summary>
    public string? Pool { get; init; }

    /// <summary>Gets the consumer-owned connection a connection session resolves.</summary>
    public string? ConnectionRef { get; init; }
}
