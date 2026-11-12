namespace HarvestSources.Api.Ocharo;

/// <summary>Explicit setup options; manual runs remain available with disabled schedules.</summary>
public sealed record OcharoSetupRequest
{
    /// <summary>Starts the six-hour capture and daily link-check cadences for newly created pipelines.</summary>
    public bool EnableSchedules { get; init; }
    /// <summary>Includes registered Instagram channels, with schedules disabled until separately configured.</summary>
    public bool IncludeInstagram { get; init; }

    /// <summary>Explicit publisher groups to set up. Null keeps the original preset selection.</summary>
    public IReadOnlyList<string>? GroupIds { get; init; }
}
