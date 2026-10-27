namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Represents what part of a source a pipeline harvests: a listing URL and filter parameters, or the whole
/// source when both are empty.</summary>
/// <remarks>The canonical form — the normalized URL and the parameters sorted by name — is the pipeline's scope key, so
/// two requests for the same filter join one pipeline.</remarks>
public sealed record PipelineScope
{
    /// <summary>Gets the listing or search URL the pipeline starts from.</summary>
    public string? Url { get; init; }

    /// <summary>Gets the filter parameters, such as a category or a city.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}
