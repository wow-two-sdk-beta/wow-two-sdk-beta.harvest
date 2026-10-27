namespace WoW.Two.Sdk.Beta.Be.Harvest.Runs;

/// <summary>Refers to a work item's place in its host's queue; a higher value is claimed first, never paced faster.</summary>
public enum WorkPriority
{
    /// <summary>History replayed to a joiner.</summary>
    Backfill = 0,

    /// <summary>An existence probe.</summary>
    Check = 1,

    /// <summary>A whole-site pass.</summary>
    Sweep = 2,

    /// <summary>New and changed posts.</summary>
    Fresh = 3,

    /// <summary>A link a person is waiting on.</summary>
    Interactive = 4,
}
