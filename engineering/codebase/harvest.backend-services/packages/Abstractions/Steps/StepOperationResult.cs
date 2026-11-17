namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Reports the result of one operation inside a work item.</summary>
/// <param name="Status">Completed, failed, skipped or interrupted, in lowercase.</param>
/// <param name="Input">The number of inputs, when measured.</param>
/// <param name="Output">The number of outputs, when measured.</param>
/// <param name="Message">A short operator-safe result, without response bodies or credentials.</param>
/// <param name="HttpStatus">The HTTP status for a fetch operation, when available.</param>
public sealed record StepOperationResult(
    string Status = "completed", int? Input = null, int? Output = null, string? Message = null, int? HttpStatus = null);
