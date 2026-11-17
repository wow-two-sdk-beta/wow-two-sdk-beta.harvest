using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Runs;

/// <summary>Records an operation as it executes inside a page or post attempt.</summary>
/// <param name="Key">The operation's stable key.</param>
/// <param name="StartedAtUtc">When the operation began.</param>
/// <param name="FinishedAtUtc">When the operation ended, if it has.</param>
/// <param name="Result">The measured outcome, if the operation has ended.</param>
public sealed record RunOperationModel(
    string Key, DateTimeOffset StartedAtUtc, DateTimeOffset? FinishedAtUtc = null, StepOperationResult? Result = null);
