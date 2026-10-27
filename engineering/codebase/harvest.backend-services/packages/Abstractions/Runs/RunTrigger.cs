namespace WoW.Two.Sdk.Beta.Be.Harvest.Runs;

/// <summary>Refers to what queued a run.</summary>
public enum RunTrigger
{
    /// <summary>A person asked for it.</summary>
    Manual,

    /// <summary>A schedule row fell due.</summary>
    Schedule,

    /// <summary>The previous run left frontier.</summary>
    Continuation,

    /// <summary>A new subscriber needed a fresh pass.</summary>
    Join,

    /// <summary>A consented submission arrived.</summary>
    Intake,

    /// <summary>A link was submitted.</summary>
    Link,
}
