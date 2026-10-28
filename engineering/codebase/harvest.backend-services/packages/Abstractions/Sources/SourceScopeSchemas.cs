using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Builds the shared URL-and-query scope schema without source-specific execution code.</summary>
public static class SourceScopeSchemas
{
    /// <summary>Describes a saved URL and string query overrides; an optional URL permits whole-source discovery.</summary>
    /// <param name="description">The adapter's public URL guidance.</param>
    /// <param name="required">Whether this adapter requires an explicit URL.</param>
    public static JsonElement WebUrl(string description, bool required = true) => JsonSerializer.SerializeToElement(new
    {
        schema = "harvest.scope.v1",
        type = "object",
        properties = new
        {
            url = new { type = "string", format = "uri", title = "Starting URL / saved filter", description },
            parameters = new { type = "object", title = "Query parameter overrides", additionalProperties = new { type = "string" } },
        },
        required = required ? new[] { "url" } : [],
        additionalProperties = false,
    });
}
