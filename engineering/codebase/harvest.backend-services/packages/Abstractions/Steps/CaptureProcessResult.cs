using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents the work outcome and the resulting data for this capture.</summary>
/// <param name="StepResult">The outcome of the processing claim.</param>
/// <param name="Output">The processor's data, if it completed with a result.</param>
/// <param name="Retryable">Whether a later run may retry a failed result for this exact processor configuration.</param>
public sealed record CaptureProcessResult(StepResult StepResult, JsonElement? Output = null, bool Retryable = true);
