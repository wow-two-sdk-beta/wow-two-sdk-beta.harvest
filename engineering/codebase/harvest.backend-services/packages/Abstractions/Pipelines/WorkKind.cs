namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Refers to a kind of work a pipeline does; each names a step plan its adapter declares.</summary>
public enum WorkKind
{
    /// <summary>Discover through the listing path, then capture each item.</summary>
    Capture,

    /// <summary>Enumerate only, writing addresses to the frontier.</summary>
    Discover,

    /// <summary>Re-capture the known frontier, least recently captured first.</summary>
    Refresh,

    /// <summary>A whole-site pass: a complete enumeration, a refresh and a check, declared as three schedule rows.</summary>
    Sweep,

    /// <summary>Probe whether known posts still exist.</summary>
    Check,

    /// <summary>Snapshot counters outside the content hash.</summary>
    Metrics,

    /// <summary>Capture one submitted URL; never scheduled.</summary>
    Link,

    /// <summary>Consented submissions; never scheduled.</summary>
    Intake,
}
