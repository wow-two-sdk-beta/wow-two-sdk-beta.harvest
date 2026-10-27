using System.Net;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Egress;

/// <summary>Represents a leased exit: the proxy endpoint and credentials a client sends through, if any.</summary>
public sealed record EgressLease
{
    /// <summary>Gets the lease's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the kind of exit.</summary>
    public required EgressKind Kind { get; init; }

    /// <summary>Gets the host the lease was taken for.</summary>
    public required string TargetHost { get; init; }

    /// <summary>Gets the pool the exit came from.</summary>
    public string? Pool { get; init; }

    /// <summary>Gets the exit's geography.</summary>
    public string Geo { get; init; } = "any";

    /// <summary>Gets the proxy endpoint; null for <see cref="EgressKind.Direct"/>.</summary>
    public Uri? Proxy { get; init; }

    /// <summary>Gets the proxy credentials; held in memory only, never stored with a capture.</summary>
    public NetworkCredential? Credentials { get; init; }

    /// <summary>Gets the key a sticky exit is bound to.</summary>
    public string? StickyKey { get; init; }

    /// <summary>Gets when the lease lapses (UTC); held until released when null.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}
