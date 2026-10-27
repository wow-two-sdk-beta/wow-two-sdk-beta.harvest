namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents a step's evidence about a known link, which the link state machine applies under its rules.</summary>
public sealed record LinkObservation
{
    /// <summary>Gets the link's identity within its source.</summary>
    public required string ExternalKey { get; init; }

    /// <summary>Gets what the step learned.</summary>
    public required LinkSignal Signal { get; init; }

    /// <summary>Gets the reason, such as <c>direct_404</c>, <c>soft_404</c> or <c>absent_from_enumeration</c>.</summary>
    public required string Reason { get; init; }

    /// <summary>Gets the HTTP status behind the evidence.</summary>
    public int? HttpStatus { get; init; }
}
