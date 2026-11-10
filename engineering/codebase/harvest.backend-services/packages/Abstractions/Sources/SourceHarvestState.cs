namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Refers to where a source's harvesting stands, decided in this order.</summary>
public enum SourceHarvestState
{
    /// <summary>One of its runs is queued or running.</summary>
    Harvesting,

    /// <summary>At least one section is harvesting and nothing needs attention.</summary>
    Healthy,

    /// <summary>Its host is paused, has failed three or more times in a row, or its last harvest failed.</summary>
    NeedsAttention,

    /// <summary>No section is harvesting.</summary>
    Idle,
}
