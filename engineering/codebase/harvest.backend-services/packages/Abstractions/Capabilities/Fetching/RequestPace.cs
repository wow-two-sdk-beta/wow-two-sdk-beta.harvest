namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;

/// <summary>Represents a pipeline's own pacing of its requests to a host: when one is set, the request reserves its start
/// time under the host gate, releases the gate and sends.</summary>
/// <remarks>The pace replaces the host's pace, the reader's gap and the two-second fetch minimum for the pipeline's own
/// requests. A robots <c>Crawl-delay</c>, a <c>Retry-After</c>, a host pause and a delay a refusal raised still bind. The gate
/// stays shared: the request's reserved gap also delays the next request of any other pipeline on the host.</remarks>
public sealed record RequestPace
{
    /// <summary>Gets the gap between the starts of the pipeline's requests to the host; the host's own gap when null.</summary>
    public TimeSpan? Gap { get; init; }
}
