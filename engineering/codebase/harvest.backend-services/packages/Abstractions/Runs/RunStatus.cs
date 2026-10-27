namespace WoW.Two.Sdk.Beta.Be.Harvest.Runs;

/// <summary>Refers to where a run stands; only <see cref="Completed"/> after a complete enumeration may infer absence.</summary>
public enum RunStatus
{
    /// <summary>Waiting to start.</summary>
    Queued,

    /// <summary>Its work items are being claimed.</summary>
    Running,

    /// <summary>The source ran out of reachable results.</summary>
    Completed,

    /// <summary>A limit, a block or failures ended it early.</summary>
    Partial,

    /// <summary>It captured nothing and failed.</summary>
    Failed,

    /// <summary>A person cancelled it.</summary>
    Cancelled,

    /// <summary>Its lease lapsed with no planner left.</summary>
    Interrupted,

    /// <summary>Waiting on a budget, a person or a permission; not terminal.</summary>
    Paused,
}
