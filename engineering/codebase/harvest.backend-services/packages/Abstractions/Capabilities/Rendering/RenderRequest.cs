namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Rendering;

/// <summary>Represents a page to render, fetched direct under Harvest's user agent.</summary>
public sealed record RenderRequest
{
    /// <summary>Gets the page to render.</summary>
    public required Uri Url { get; init; }

    /// <summary>Gets the hard limit on the render.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
