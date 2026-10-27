using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Profiles;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Sessions;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents the capabilities a step needs as a floor, a ladder and a ceiling, plus its session, render,
/// profile and challenge policy.</summary>
/// <remarks>The resolver picks the rung at claim from the lane's learned state, clamped to <c>[Floor, Ceiling]</c>; rung
/// names are <see cref="CapabilityRungs"/>.</remarks>
public sealed record StepRequirements
{
    /// <summary>Gets what the step needs of its exit.</summary>
    public EgressNeed Egress { get; init; } = new();

    /// <summary>Gets the session the step needs.</summary>
    public SessionNeed Session { get; init; } = new() { Kind = SessionKind.None };

    /// <summary>Gets whether the step needs a rendering browser.</summary>
    public RenderNeed Render { get; init; } = RenderNeed.Never;

    /// <summary>Gets the request profile name.</summary>
    public string Profile { get; init; } = ProfileConstants.Bot;

    /// <summary>Gets how far the step may go past a challenge.</summary>
    public ChallengePolicy Challenge { get; init; } = new();

    /// <summary>Gets the cheapest rung the step may use.</summary>
    public string Floor { get; init; } = CapabilityRungs.R0;

    /// <summary>Gets the rungs the step may climb, cheapest first.</summary>
    public IReadOnlyList<string> Ladder { get; init; } = [CapabilityRungs.R0];

    /// <summary>Gets the dearest rung the step may reach.</summary>
    public string Ceiling { get; init; } = CapabilityRungs.R0;
}
