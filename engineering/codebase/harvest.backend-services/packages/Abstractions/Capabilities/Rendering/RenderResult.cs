namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Rendering;

/// <summary>Represents a rendered page: its DOM, the JSON it fetched and the session state it left.</summary>
public sealed record RenderResult
{
    /// <summary>Gets the URL that answered, after redirects.</summary>
    public required Uri FinalUrl { get; init; }

    /// <summary>Gets the main document's HTTP status.</summary>
    public required int StatusCode { get; init; }

    /// <summary>Gets the rendered DOM as HTML.</summary>
    public required string Html { get; init; }

    /// <summary>Gets the JSON responses the page fetched.</summary>
    public IReadOnlyList<RenderedResponse> Responses { get; init; } = [];

    /// <summary>Gets the browser storage state after the render, as JSON.</summary>
    public string? StorageState { get; init; }
}
