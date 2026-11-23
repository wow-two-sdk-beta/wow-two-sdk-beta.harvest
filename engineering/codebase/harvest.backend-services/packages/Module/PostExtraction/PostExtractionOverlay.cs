using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Holds the stored event format, first occurrence and whether the answer identifies a current event.</summary>
internal sealed record StoredEventExtraction(string? Format, EventOccurrence? First, bool IdentifiesCurrentEvent)
{
    /// <summary>Reads the stored columns, or returns null when no answer exists.</summary>
    /// <param name="exists">Whether a done answer exists.</param>
    /// <param name="format">The answer's <c>format</c>.</param>
    /// <param name="first">The answer's first occurrence as JSON, or null.</param>
    /// <param name="identifiesCurrentEvent">Whether the current answer names an event the post announces, updates or reports on.</param>
    internal static StoredEventExtraction? Read(bool exists, string? format, string? first, bool identifiesCurrentEvent = false)
    {
        if (!exists)
            return null;
        EventOccurrence? occurrence = null;
        if (first is not null)
        {
            try
            {
                occurrence = JsonSerializer.Deserialize<EventOccurrence>(first, EvidenceWire.Options);
            }
            catch (JsonException)
            {
                occurrence = null;
            }
        }
        return new(format, occurrence, identifiesCurrentEvent);
    }
}

/// <summary>Fills an unknown kind for an identified, dated event and adds estimated dates when the post states no start.</summary>
/// <remarks>A stated start always wins: the readers' dates are resolved first, so a later capture that states one
/// replaces the estimate on its next resolve. The end follows the same rule on its own.</remarks>
internal static class PostExtractionOverlay
{
    /// <summary>Applies the event kind and date evidence where the source reading leaves them open.</summary>
    internal static PostEnvelope Apply(PostEnvelope resolved, StoredEventExtraction? stored)
    {
        if (stored?.First is not { Starts.Length: > 0 } occurrence)
            return resolved;
        if (resolved.Kind is null && stored.IdentifiesCurrentEvent)
            resolved = resolved with { Kind = PostKind.Event };
        if (resolved.Dates.Any(date => date.Role == DateRole.Start))
            return resolved;
        var dates = resolved.Dates.ToList();
        dates.Add(Estimate(DateRole.Start, occurrence.Starts, occurrence));
        if (occurrence.Ends is { Length: > 0 } ends && !resolved.Dates.Any(date => date.Role == DateRole.End))
            dates.Add(Estimate(DateRole.End, ends, occurrence));
        return resolved with { Dates = dates };
    }

    private static PostDate Estimate(DateRole role, string value, EventOccurrence occurrence) => new()
    {
        Role = role, Value = value, Precision = occurrence.Precision, Raw = occurrence.Evidence, Certainty = DateCertainty.Estimated,
    };
}
