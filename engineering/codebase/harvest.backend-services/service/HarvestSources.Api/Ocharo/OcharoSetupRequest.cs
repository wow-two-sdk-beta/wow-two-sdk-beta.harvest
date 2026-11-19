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

    /// <summary>Also provisions the tenant's <c>ocharo-events</c> selector subscription from the shipped selector file; every
    /// publisher it lists must already have a source group in the container.</summary>
    public bool Selector { get; init; }

    /// <summary>Puts the shipped selector back on an existing <c>ocharo-events</c> subscription, a change by
    /// <c>ocharo setup</c>; needs <see cref="Selector"/>.</summary>
    public bool SelectorReset { get; init; }
}
