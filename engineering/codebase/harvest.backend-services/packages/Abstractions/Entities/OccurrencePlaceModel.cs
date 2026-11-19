namespace WoW.Two.Sdk.Beta.Be.Harvest.Entities;

/// <summary>Represents where an occurrence takes place, as sources named it.</summary>
public sealed record OccurrencePlaceModel
{
    /// <summary>Gets the venue.</summary>
    public string? Venue { get; init; }

    /// <summary>Gets the city.</summary>
    public string? City { get; init; }

    /// <summary>Gets the country.</summary>
    public string? Country { get; init; }
}
