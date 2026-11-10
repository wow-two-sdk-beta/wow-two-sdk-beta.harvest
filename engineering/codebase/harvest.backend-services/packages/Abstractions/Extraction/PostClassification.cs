namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents a classification step's verdict on a post: whether it is relevant and which kind it is.</summary>
/// <remarks>
///   - <c>wow2.event.v1</c> carries it as <c>classification</c>, and its <see cref="Kind"/> as the top-level <c>kind</c>
///   - Events reads it under its <c>payload</c> classifier: a relevant verdict below the source's minimum confidence,
///     or an uncertain one, decides nothing
///   - no SCFD step writes it yet; the model tier (events-integration #55) is its first writer
/// </remarks>
public sealed record PostClassification
{
    /// <summary>Gets the step that classified the post, such as <c>model:{id}@{prompt}</c>.</summary>
    public required string Classifier { get; init; }

    /// <summary>Gets the verdict.</summary>
    public required ClassificationVerdict Verdict { get; init; }

    /// <summary>Gets the verdict's confidence, from 0 to 1, when the step gives one.</summary>
    public double? Confidence { get; init; }

    /// <summary>Gets a short reason for the verdict.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the post's kind in the subscriber's taxonomy, lowercase, such as <c>runway</c>.</summary>
    public string? Kind { get; init; }
}
