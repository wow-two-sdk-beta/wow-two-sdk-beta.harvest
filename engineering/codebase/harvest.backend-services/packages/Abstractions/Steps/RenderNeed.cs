namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Refers to whether a step needs a rendering browser to show a page's content.</summary>
/// <remarks>Rendering answers a JavaScript shell, never a challenge, a block or a rate limit.</remarks>
public enum RenderNeed
{
    /// <summary>A static fetch is enough.</summary>
    Never,

    /// <summary>Render only when a static fetch reports <c>render-required</c>.</summary>
    Fallback,

    /// <summary>Always render.</summary>
    Required,
}
