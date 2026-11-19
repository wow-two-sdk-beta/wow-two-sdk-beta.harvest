using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Represents what a post's resolution reads from a done <c>extract-event</c> answer: its <c>format</c> and its
/// first occurrence.</summary>
internal sealed record StoredEventExtraction(string? Format, EventOccurrence? First)
{
    /// <summary>Reads the stored columns, or returns null when no answer exists.</summary>
    /// <param name="exists">Whether a done answer exists.</param>
    /// <param name="format">The answer's <c>format</c>.</param>
    /// <param name="first">The answer's first occurrence as JSON, or null.</param>
    internal static StoredEventExtraction? Read(bool exists, string? format, string? first)
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
        return new(format, occurrence);
    }
}

/// <summary>Writes an <c>extract-event</c> answer's first occurrence onto a resolved envelope as estimated dates, when the
/// post states no start (events-integration S17).</summary>
/// <remarks>A stated start always wins: the readers' dates are resolved first, so a later capture that states one
/// replaces the estimate on its next resolve. The end follows the same rule on its own.</remarks>
internal static class PostExtractionOverlay
{
    /// <summary>Returns <paramref name="resolved"/> with an estimated <see cref="DateRole.Start"/> (and
    /// <see cref="DateRole.End"/>, when the occurrence has one and the post states none), quoting the occurrence's
    /// evidence; unchanged when there is no occurrence or the post states its start.</summary>
    internal static PostEnvelope Apply(PostEnvelope resolved, StoredEventExtraction? stored)
    {
        if (stored?.First is not { Starts.Length: > 0 } occurrence || resolved.Dates.Any(date => date.Role == DateRole.Start))
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
