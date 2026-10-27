namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Refers to what a step does; render is a capability a step requires, never a step.</summary>
public enum StepRole
{
    /// <summary>Lists what exists: detail targets, the next page, an enumeration-complete flag.</summary>
    Enumerate,

    /// <summary>Fetches one post: a page or an API object; yields a capture and contact, media and thread targets.</summary>
    Detail,

    /// <summary>Reveals a hidden contact point.</summary>
    Contact,

    /// <summary>Copies images and video into file storage.</summary>
    Media,

    /// <summary>Reads comments and replies.</summary>
    Thread,

    /// <summary>Probes whether a post still exists.</summary>
    Check,

    /// <summary>Snapshots counters outside the content hash.</summary>
    Metrics,

    /// <summary>Submits, polls and collects a licensed provider's run.</summary>
    ProviderJob,

    /// <summary>Runs the extraction cascade.</summary>
    Extract,
}
