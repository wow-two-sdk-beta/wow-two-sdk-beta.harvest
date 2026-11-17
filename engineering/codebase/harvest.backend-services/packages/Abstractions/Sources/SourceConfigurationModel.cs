using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Public display metadata supplied by an adapter; never contains provider credentials or session values.</summary>
public sealed record SourceConfigurationModel
{
    /// <summary>Gets the human-readable adapter name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Gets whether local configuration is sufficient to attempt execution.</summary>
    public SourceReadiness Readiness { get; init; } = SourceReadiness.Ready;
    /// <summary>Gets an operator-facing configuration message without secret values.</summary>
    public string? Message { get; init; }
    /// <summary>Gets a declarative JSON Schema for the saved pipeline scope. Execution remains authoritative.</summary>
    public required JsonElement ScopeSchema { get; init; }
    /// <summary>Gets the JSON Schema for this adapter's native <c>sourceFields</c> envelope, if it declares one.</summary>
    public JsonElement? SourceFieldsSchema { get; init; }
    /// <summary>Gets the optional facets this adapter supports over its stored posts.</summary>
    public IReadOnlyList<SourcePostFilterDefinition> PostFilters { get; init; } = [];
    /// <summary>Gets the optional capture-evidence sections this source displays in the console.</summary>
    public SourcePostView PostView { get; init; } = new();
}

/// <summary>Chooses which evidence belongs on the source's captured-post page.</summary>
public sealed record SourcePostView
{
    /// <summary>Gets whether captured photos appear in the photo gallery.</summary>
    public bool ShowPhotos { get; init; } = true;
    /// <summary>Gets whether the captured source text appears below the capture record.</summary>
    public bool ShowSourceText { get; init; }
}
