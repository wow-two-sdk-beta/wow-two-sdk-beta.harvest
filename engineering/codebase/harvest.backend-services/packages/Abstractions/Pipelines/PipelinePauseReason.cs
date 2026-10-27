namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Refers to why a pipeline is paused.</summary>
public enum PipelinePauseReason
{
    /// <summary>An operator paused it.</summary>
    Operator,

    /// <summary>A hard budget cap stopped it.</summary>
    BudgetHard,

    /// <summary>The source's permission no longer allows it.</summary>
    Permission,

    /// <summary>An account or a connection it needs is down.</summary>
    Account,

    /// <summary>Its last channel subscriber left and it is not pinned.</summary>
    Idle,
}
