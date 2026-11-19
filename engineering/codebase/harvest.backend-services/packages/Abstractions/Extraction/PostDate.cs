namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents a date a post states, with its meaning, precision and the text it came from.</summary>
public sealed record PostDate
{
    /// <summary>Gets what the date means.</summary>
    public required DateRole Role { get; init; }

    /// <summary>Gets the value as a partial ISO 8601 string; <c>--03-14</c> when the year is absent.</summary>
    public required string Value { get; init; }

    /// <summary>Gets the end of a range, in the same form.</summary>
    public string? End { get; init; }

    /// <summary>Gets the finest unit the value states.</summary>
    public required DatePrecision Precision { get; init; }

    /// <summary>Gets the text the date was read from.</summary>
    public string? Raw { get; init; }

    /// <summary>Gets whether the year was inferred rather than stated.</summary>
    public bool YearInferred { get; init; }

    /// <summary>Gets, for a <see cref="DateRole.Deadline"/>, whether the call it closes is open, closed or unknown as the
    /// source states it; null for other roles.</summary>
    public CallStatus? Status { get; init; }

    /// <summary>Gets whether the post states the date or it was estimated; null reads as
    /// <see cref="DateCertainty.Stated"/>, so a reader's date carries no value.</summary>
    /// <remarks>Only the extraction pass writes <see cref="DateCertainty.Estimated"/>: a post that states no start gets
    /// its <c>extract-event</c> answer's first occurrence, and a stated date always replaces it.</remarks>
    public DateCertainty? Certainty { get; init; }
}
