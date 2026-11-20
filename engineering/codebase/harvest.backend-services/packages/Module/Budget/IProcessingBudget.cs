namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Budget;

/// <summary>Reads how much of the AI spend limit the host has used in the current window, so every pass that calls a model
/// can slow down and then stop before a credit runs out.</summary>
/// <remarks>One host has one inference log; two hosts that share a provider key each count only their own, so their limits
/// must add up to the credit.</remarks>
public interface IProcessingBudget
{
    /// <summary>Reads the spend of the current window, reusing a computed reading for
    /// <see cref="ProcessingBudgetOptions.RefreshInterval"/>.</summary>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The reading; never a failure. A log that cannot be read keeps the last good reading, or with none fails
    /// closed at <see cref="BudgetState.Hard"/> (<see cref="BudgetReading.ReadFailed"/>).</returns>
    Task<BudgetReading> ReadAsync(CancellationToken cancellationToken = default);
}
