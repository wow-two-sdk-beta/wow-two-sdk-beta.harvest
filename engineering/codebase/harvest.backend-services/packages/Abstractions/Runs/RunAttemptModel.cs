namespace WoW.Two.Sdk.Beta.Be.Harvest.Runs;

/// <summary>Records one claim of a run step.</summary>
/// <param name="WorkItemId">The work item this attempt tried.</param>
/// <param name="Step">The step that ran.</param>
/// <param name="Target">The catalog or listing it tried.</param>
/// <param name="Attempt">The attempt number, starting at one.</param>
/// <param name="StartedAtUtc">When the worker claimed the step.</param>
/// <param name="FinishedAtUtc">When the attempt ended, if it has.</param>
/// <param name="Outcome">The machine outcome, if the attempt ended.</param>
/// <param name="Message">A short diagnostic for the outcome.</param>
/// <param name="RetryAtUtc">The host backoff or retry boundary, when known.</param>
/// <param name="HttpStatus">The marketplace status, when one was received.</param>
/// <param name="Discovered">How many listing targets the attempt found.</param>
public sealed record RunAttemptModel(Guid WorkItemId, string Step, string Target, int Attempt, DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc, string? Outcome, string? Message, DateTimeOffset? RetryAtUtc, short? HttpStatus, int Discovered)
{
    /// <summary>Gets the operations recorded inside this attempt, in the order they started.</summary>
    public IReadOnlyList<RunOperationModel> Operations { get; init; } = [];
}
