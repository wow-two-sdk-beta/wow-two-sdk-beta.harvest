namespace WoW.Two.Sdk.Beta.Be.Harvest.Runs;

/// <summary>Refers to where a claimable work item stands.</summary>
public enum WorkItemState
{
    /// <summary>Waiting for its host and lane to allow a claim.</summary>
    Queued,

    /// <summary>Claimed under a lease.</summary>
    Running,

    /// <summary>Its result was written.</summary>
    Done,

    /// <summary>It failed after its retries.</summary>
    Failed,

    /// <summary>Its target no longer exists — a finding, not a failure.</summary>
    Gone,

    /// <summary>A block, a budget or a missing capability stopped it.</summary>
    Blocked,
}
