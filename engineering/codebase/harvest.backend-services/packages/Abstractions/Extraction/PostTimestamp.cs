namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents when a post was published, as precise as the source said.</summary>
public sealed record PostTimestamp
{
    /// <summary>Gets the value as a partial ISO 8601 string: <c>2026-03-14T19:00</c>, <c>2026-03-14</c>, <c>2026-03</c>.</summary>
    public required string Value { get; init; }

    /// <summary>Gets the finest unit the value states.</summary>
    public required DatePrecision Precision { get; init; }

    /// <summary>Gets the IANA time zone the value reads in, when known.</summary>
    public string? TimeZone { get; init; }
}
