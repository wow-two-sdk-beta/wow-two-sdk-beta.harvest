using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;

/// <summary>Reads the application deadline a named reader records in its <c>schemaOrg.{reader}</c> namespace.</summary>
/// <remarks>The reader namespace stays as recorded; this only copies a stated date into a typed
/// <see cref="DateRole.Deadline"/>, so a deadline the source does not state never appears.</remarks>
internal static class ApplicationDeadlines
{
    /// <summary>Returns the stated application deadline of an Event node, or null when no reader states one.</summary>
    /// <param name="schemaOrg">The captured schema.org Event node.</param>
    internal static PostDate? Read(JsonElement schemaOrg)
    {
        if (schemaOrg.ValueKind != JsonValueKind.Object) return null;
        if (Evidence(schemaOrg, "craftfair") is { } craftfair)
            return Deadline(ReaderSupport.String(craftfair, "applicationDeadline"), ReaderSupport.String(craftfair, "applicationDeadlineRaw"),
                Status(ReaderSupport.String(craftfair, "applicationStatusRaw")));
        if (Evidence(schemaOrg, "uzexpocentre") is { } uzexpocentre)
            return Deadline(ReaderSupport.String(uzexpocentre, "applicationDeadline"), null, CallStatus.Unknown);
        if (Evidence(schemaOrg, "afisha") is { } afisha && afisha.TryGetProperty("applicationDeadlineEvidence", out var evidence)
            && evidence.ValueKind == JsonValueKind.Object)
            return Deadline(ReaderSupport.String(evidence, "date"), ReaderSupport.String(evidence, "raw"), CallStatus.Unknown);
        return null;
    }

    private static JsonElement? Evidence(JsonElement node, string reader) =>
        node.TryGetProperty(reader, out var evidence) && evidence.ValueKind == JsonValueKind.Object ? evidence : null;

    private static PostDate? Deadline(string? value, string? raw, CallStatus status) =>
        ExtractionRules.Date(value, DateRole.Deadline) is { } date
            ? date with { Raw = string.IsNullOrWhiteSpace(raw) ? date.Raw : raw.Trim(), Status = status } : null;

    /// <summary>Reads a status badge: "closed" wins over "open"; anything else stays unknown.</summary>
    private static CallStatus Status(string? raw) =>
        raw is null ? CallStatus.Unknown
        : raw.Contains("closed", StringComparison.OrdinalIgnoreCase) ? CallStatus.Closed
        : raw.Contains("open", StringComparison.OrdinalIgnoreCase) ? CallStatus.Open
        : CallStatus.Unknown;
}
