using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Egress;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Profiles;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Sessions;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents the capabilities resolved for one claim: the rung, its leases and the profile.</summary>
public sealed record ResolvedPlan
{
    /// <summary>Gets the step being executed.</summary>
    public required string StepKey { get; init; }

    /// <summary>Gets the lane the item was claimed in.</summary>
    public required string Lane { get; init; }

    /// <summary>Gets the host the item was claimed for.</summary>
    public required string HostKey { get; init; }

    /// <summary>Gets the rung in use, within the step's floor and ceiling.</summary>
    public required string Rung { get; init; }

    /// <summary>Gets the requirements after the adapter, source and pipeline layers.</summary>
    public required StepRequirements Requirements { get; init; }

    /// <summary>Gets the request profile.</summary>
    public required RequestProfile Profile { get; init; }

    /// <summary>Gets the exit leased for the claim.</summary>
    public EgressLease? Egress { get; init; }

    /// <summary>Gets the session leased for the claim.</summary>
    public SessionLease? Session { get; init; }
}
