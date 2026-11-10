using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents a source's newest run that a refusal or a challenge stopped.</summary>
public sealed record SourceRefusalModel
{
    /// <summary>Gets when the run finished, else when it was queued (UTC).</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>Gets what the host answered.</summary>
    public required FetchOutcome Outcome { get; init; }

    /// <summary>Gets the run's last progress message.</summary>
    public string? Message { get; init; }
}
