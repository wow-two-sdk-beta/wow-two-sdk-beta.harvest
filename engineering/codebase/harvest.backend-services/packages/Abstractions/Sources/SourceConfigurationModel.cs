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
}
