namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents coordinates, only when the source gives them.</summary>
public sealed record GeoPoint
{
    /// <summary>Gets the latitude in degrees.</summary>
    public required double Latitude { get; init; }

    /// <summary>Gets the longitude in degrees.</summary>
    public required double Longitude { get; init; }
}
