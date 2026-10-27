namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities;

/// <summary>Holds the escalation ladder's rungs, cheapest first; a step's floor, ladder and ceiling name them.</summary>
/// <remarks>The order of <see cref="Ordered"/> is the ladder: a lane climbs one rung after repeated challenges or blocks,
/// never on a rate limit, and a probe brings it back down.</remarks>
public static class CapabilityRungs
{
    /// <summary>Holds direct egress with the identifying <c>bot</c> profile.</summary>
    public const string R0 = "R0";

    /// <summary>Holds direct egress with a coherent browser profile.</summary>
    public const string R1 = "R1";

    /// <summary>Holds rotating datacenter egress.</summary>
    public const string R2 = "R2";

    /// <summary>Holds rotating residential egress.</summary>
    public const string R3 = "R3";

    /// <summary>Holds sticky residential egress with a warm cookie jar.</summary>
    public const string R4 = "R4";

    /// <summary>Holds a rendering browser over sticky residential egress.</summary>
    public const string R5 = "R5";

    /// <summary>Holds a vendor unlocker that brings its own egress, fingerprint, render and solving.</summary>
    public const string R6 = "R6";

    /// <summary>Holds a captcha solver over sticky residential egress or the browser.</summary>
    public const string R7 = "R7";

    /// <summary>Holds a hand-over to an operator who passes the check.</summary>
    public const string R8 = "R8";

    /// <summary>Gets every rung, cheapest first.</summary>
    public static IReadOnlyList<string> Ordered { get; } = [R0, R1, R2, R3, R4, R5, R6, R7, R8];
}
