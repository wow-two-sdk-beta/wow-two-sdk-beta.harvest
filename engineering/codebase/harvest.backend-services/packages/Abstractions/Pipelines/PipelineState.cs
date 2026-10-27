namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Refers to a pipeline's lifecycle.</summary>
public enum PipelineState
{
    /// <summary>Suggested, not yet approved or declared.</summary>
    Proposed,

    /// <summary>Waiting for an operator, such as on a restricted source.</summary>
    PendingApproval,

    /// <summary>Scheduled and running.</summary>
    Active,

    /// <summary>Stopped for a reason in <see cref="PipelinePauseReason"/>.</summary>
    Paused,

    /// <summary>Closed; its posts follow their retention.</summary>
    Archived,
}
