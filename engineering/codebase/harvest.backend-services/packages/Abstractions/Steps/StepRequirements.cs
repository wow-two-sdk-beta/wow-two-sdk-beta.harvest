using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents what a step needs to read its content: a static fetch or a rendering browser, and how slowly it
/// paces its host.</summary>
/// <remarks>Nothing here gets past a host's defences: a challenge, a block or a rate limit is an outcome that backs the
/// host off.</remarks>
public sealed record StepRequirements
{
    /// <summary>Gets whether the step needs a rendering browser, for pages that show their content only through JavaScript.</summary>
    public RenderNeed Render { get; init; } = RenderNeed.Never;

    /// <summary>Gets the least gap between the step's requests to one host; the host's own pacing when null, and the
    /// longer of the two wins.</summary>
    public TimeSpan? MinDelay { get; init; }

    /// <summary>Gets the pipeline's own request pacing, set by the engine from the stage's gap and parallel fetches; null keeps
    /// the host's pacing.</summary>
    public RequestPace? Pace { get; init; }
}
