namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Refers to the finest unit a published date states; "September" stays a month, never 1 September.</summary>
public enum DatePrecision
{
    /// <summary>A year.</summary>
    Year,

    /// <summary>A month.</summary>
    Month,

    /// <summary>A day.</summary>
    Day,

    /// <summary>An hour.</summary>
    Hour,

    /// <summary>A minute.</summary>
    Minute,

    /// <summary>A second.</summary>
    Second,
}
