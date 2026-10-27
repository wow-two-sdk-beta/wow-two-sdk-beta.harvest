namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents a price as published; unknown is null, never zero, and free is its own flag.</summary>
public sealed record PostPrice
{
    /// <summary>Gets the single amount.</summary>
    public decimal? Amount { get; init; }

    /// <summary>Gets the lower bound of a range.</summary>
    public decimal? Min { get; init; }

    /// <summary>Gets the upper bound of a range.</summary>
    public decimal? Max { get; init; }

    /// <summary>Gets the currency as published or as ISO 4217, such as <c>UZS</c> or <c>USD</c>.</summary>
    public string? Currency { get; init; }

    /// <summary>Gets the period the price is per, such as <c>month</c>.</summary>
    public string? Per { get; init; }

    /// <summary>Gets whether the price is negotiable.</summary>
    public bool? Negotiable { get; init; }

    /// <summary>Gets whether the thing is free.</summary>
    public bool Free { get; init; }

    /// <summary>Gets the text the price was read from.</summary>
    public string? Raw { get; init; }

    /// <summary>Gets what the price is.</summary>
    public PriceRole Role { get; init; } = PriceRole.Listed;
}
