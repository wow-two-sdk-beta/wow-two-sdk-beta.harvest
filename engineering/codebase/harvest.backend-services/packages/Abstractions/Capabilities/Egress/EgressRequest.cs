namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Egress;

/// <summary>Represents a request for an egress lease.</summary>
public sealed record EgressRequest
{
    /// <summary>Gets the kind of exit wanted.</summary>
    public required EgressKind Kind { get; init; }

    /// <summary>Gets the host the requests will reach, for per-host health.</summary>
    public required string TargetHost { get; init; }

    /// <summary>Gets the named pool to lease from; the kind's default pool when null.</summary>
    public string? Pool { get; init; }

    /// <summary>Gets the exit's geography: a country code such as <c>UZ</c>, a comma-separated list, or <c>any</c>.</summary>
    public string Geo { get; init; } = "any";

    /// <summary>Gets whether consecutive requests keep one exit.</summary>
    public EgressStickiness Stickiness { get; init; } = EgressStickiness.Rotating;

    /// <summary>Gets the key a sticky exit binds to, such as a run, a host or an account; required when sticky.</summary>
    public string? StickyKey { get; init; }

    /// <summary>Gets how long the lease is wanted; the pool's default when null.</summary>
    public TimeSpan? TimeToLive { get; init; }
}
