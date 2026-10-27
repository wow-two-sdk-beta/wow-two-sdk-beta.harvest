using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Egress;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents what a step needs of its exit beyond the rung's kind: geography and stickiness.</summary>
public sealed record EgressNeed
{
    /// <summary>Gets the exit's geography: a country code such as <c>UZ</c>, a list, or <c>any</c>.</summary>
    public string Geo { get; init; } = "any";

    /// <summary>Gets whether consecutive requests keep one exit; the rung's default when null.</summary>
    public EgressStickiness? Stickiness { get; init; }
}
