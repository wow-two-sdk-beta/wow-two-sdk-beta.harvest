using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents what an extractor needs beside the capture: the source family and the AI gates.</summary>
/// <remarks>The model ceiling is the lowest gate: the pipeline switch, the source's <c>ai_allowed</c>, the host signal
/// and the budget. Deterministic and rule tiers are never gated.</remarks>
public sealed record ExtractionRequest
{
    /// <summary>Gets the family the capture came from.</summary>
    public required SourceFamily Family { get; init; }

    /// <summary>Gets the pipeline's AI switch.</summary>
    public AiMode Ai { get; init; } = AiMode.Off;

    /// <summary>Gets whether the source allows model tiers.</summary>
    public bool AiAllowed { get; init; }

    /// <summary>Gets whether a budget still allows model calls.</summary>
    public bool BudgetAllowsAi { get; init; }

    /// <summary>Gets the kind the source defaults to, such as <see cref="PostKind.Event"/> for an events site.</summary>
    public PostKind? DefaultKind { get; init; }
}
