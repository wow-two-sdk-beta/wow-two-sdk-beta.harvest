using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents the operator's capability limits for one step of a source, layered over the adapter's defaults.</summary>
/// <remarks>Every member is optional: a null keeps the adapter's value. Rung names are <see cref="Capabilities.CapabilityRungs"/>.</remarks>
public sealed record StepCapabilityPolicy
{
    /// <summary>Gets the lane the step runs in, so its blocks never pause the host's other steps.</summary>
    public string? Lane { get; init; }

    /// <summary>Gets the cheapest rung the step may use.</summary>
    public string? Floor { get; init; }

    /// <summary>Gets the rungs the step may climb, cheapest first.</summary>
    public IReadOnlyList<string>? Ladder { get; init; }

    /// <summary>Gets the dearest rung the step may reach.</summary>
    public string? Ceiling { get; init; }

    /// <summary>Gets the request profile, such as <c>bot</c> or <c>browser:chromium-desktop</c>.</summary>
    public string? Profile { get; init; }

    /// <summary>Gets the session the step needs.</summary>
    public SessionNeed? Session { get; init; }

    /// <summary>Gets the egress pool paid rungs lease from.</summary>
    public string? EgressPool { get; init; }

    /// <summary>Gets the egress geography: <c>UZ</c>, a country list, or <c>any</c>.</summary>
    public string? Geo { get; init; }

    /// <summary>Gets the most a thousand solves may cost (USD); solving stays off when null.</summary>
    public decimal? SolverMaxUsdPer1k { get; init; }

    /// <summary>Gets how many solves a day the step may spend.</summary>
    public int? SolverMaxSolvesPerDay { get; init; }

    /// <summary>Gets the step's monthly spending cap (USD).</summary>
    public decimal? MaxUsdPerMonth { get; init; }
}
