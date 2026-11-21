namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Represents the automatic turn-off of a pipeline's harvesting schedules after failed runs in a row.</summary>
public sealed record PipelineAutoOffModel
{
    /// <summary>Gets when the schedules turned off (UTC).</summary>
    public required DateTimeOffset AtUtc { get; init; }

    /// <summary>Gets the run that reached the count.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the failed runs in a row it took.</summary>
    public required int Failures { get; init; }

    /// <summary>Gets that run's fetch outcome (<c>blocked</c>, <c>challenge</c>, <c>rate_limited</c>, <c>network_error</c>, ...), else its status.</summary>
    public required string Reason { get; init; }
}
