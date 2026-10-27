namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Rendering;

/// <summary>Represents a JSON response the rendered page fetched, kept for readers that prefer the data to the DOM.</summary>
public sealed record RenderedResponse
{
    /// <summary>Gets the URL the page fetched.</summary>
    public required Uri Url { get; init; }

    /// <summary>Gets the response's media type.</summary>
    public string? ContentType { get; init; }

    /// <summary>Gets the response body.</summary>
    public required string Body { get; init; }
}
