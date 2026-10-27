using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Egress;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Rendering;

/// <summary>Represents a page to render, with the exit, session state and profile one browser context binds.</summary>
public sealed record RenderRequest
{
    /// <summary>Gets the page to render.</summary>
    public required Uri Url { get; init; }

    /// <summary>Gets the exit the browser sends through; credentials travel with the call and are never stored.</summary>
    public EgressLease? Egress { get; init; }

    /// <summary>Gets the browser storage state to start from, as JSON.</summary>
    public string? StorageState { get; init; }

    /// <summary>Gets the profile name; the browser's own when null.</summary>
    public string? Profile { get; init; }

    /// <summary>Gets the hard limit on the render.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
