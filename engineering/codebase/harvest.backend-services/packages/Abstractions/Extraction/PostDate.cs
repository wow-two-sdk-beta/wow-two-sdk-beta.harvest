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
}
