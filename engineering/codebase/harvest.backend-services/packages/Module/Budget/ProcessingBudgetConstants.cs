namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Budget;

/// <summary>Holds the names the spend guard shares with hosts and the passes it gates.</summary>
public static class ProcessingBudgetConstants
{
    /// <summary>The host configuration section of <see cref="ProcessingBudgetOptions"/>.</summary>
    public const string SectionName = "Harvest:Processing:Budget";

    /// <summary>The reason a gated pass stores, or a stage reports, in place of a model call when the guard is at
    /// <see cref="BudgetState.Hard"/>.</summary>
    public const string ExhaustedReason = "budget-exhausted";
}
