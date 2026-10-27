namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Egress;

/// <summary>Represents what a lease carried, reported when it is released, for the usage meters.</summary>
public sealed record EgressUsage
{
    /// <summary>Gets the requests sent through the lease.</summary>
    public long Requests { get; init; }

    /// <summary>Gets the bytes sent and received through the lease.</summary>
    public long Bytes { get; init; }
}
