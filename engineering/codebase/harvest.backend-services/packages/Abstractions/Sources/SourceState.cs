namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Refers to a source's availability; an unavailable source pauses its link checks and marks nothing gone.</summary>
public enum SourceState
{
    /// <summary>The source answers and is scheduled.</summary>
    Active,

    /// <summary>The operator paused the source.</summary>
    Paused,

    /// <summary>The source does not answer, such as a removed channel.</summary>
    Unavailable,

    /// <summary>The source's public preview is gone, so it needs another route.</summary>
    PreviewUnavailable,

    /// <summary>The source's quota ran out for the current window.</summary>
    QuotaExhausted,

    /// <summary>The source left the registry; its posts keep their last state.</summary>
    Retired,
}
