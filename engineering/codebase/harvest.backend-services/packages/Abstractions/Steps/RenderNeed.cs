namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Refers to whether a step needs a rendering browser.</summary>
public enum RenderNeed
{
    /// <summary>A static fetch is enough.</summary>
    Never,

    /// <summary>Render only when a static fetch reports <c>render-required</c>.</summary>
    Fallback,

    /// <summary>Always render.</summary>
    Required,
}
