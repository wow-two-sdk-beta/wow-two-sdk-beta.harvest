namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Represents what part of a source a pipeline harvests: a listing URL and filter parameters, or the whole
/// source when both are empty.</summary>
/// <remarks>The canonical form — the normalized URL and the parameters sorted by name — is the pipeline's scope key, so
/// two requests for the same filter join one pipeline.</remarks>
public sealed record PipelineScope
{
    /// <summary>Gets the listing or search URL the pipeline starts from.</summary>
    public string? Url { get; init; }

    /// <summary>Gets whether work targets must stay under the starting URL's path.</summary>
    public bool RestrictToPath { get; init; }

    /// <summary>Gets the filter parameters, such as a category or a city.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

    /// <summary>Checks the destination against an explicit section boundary.</summary>
    public bool IncludesUrl(string target)
    {
        if (!RestrictToPath) return true;
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var root) || !Uri.TryCreate(target, UriKind.Absolute, out var url)
            || root.Scheme != url.Scheme || root.Port != url.Port || root.IdnHost != url.IdnHost)
            return false;
        string path = root.AbsolutePath.TrimEnd('/');
        return url.AbsolutePath.TrimEnd('/') == path || url.AbsolutePath.StartsWith(path + "/", StringComparison.Ordinal);
    }
}
