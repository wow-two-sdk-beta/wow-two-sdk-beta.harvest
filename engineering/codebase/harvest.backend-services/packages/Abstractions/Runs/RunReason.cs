namespace WoW.Two.Sdk.Beta.Be.Harvest.Runs;

/// <summary>Refers to why a run stopped — the first of the three outcome levels.</summary>
public enum RunReason
{
    /// <summary>Reachable results ran out.</summary>
    Exhausted,

    /// <summary>The source listed nothing.</summary>
    NoItems,

    /// <summary>The page limit was hit.</summary>
    PageLimit,

    /// <summary>The item limit was hit.</summary>
    ItemLimit,

    /// <summary>The request budget was hit.</summary>
    RequestLimit,

    /// <summary>The byte budget was hit.</summary>
    ByteLimit,

    /// <summary>The time budget was hit.</summary>
    TimeLimit,

    /// <summary>A listing page repeated.</summary>
    RepeatedPage,

    /// <summary>A listing page repeated its items.</summary>
    RepeatedItems,

    /// <summary>Too many detail fetches failed.</summary>
    DetailFailures,

    /// <summary>A hard budget cap stopped paid work.</summary>
    BudgetHard,

    /// <summary>A provider or platform quota ran out.</summary>
    QuotaExhausted,

    /// <summary>The schedule's window closed.</summary>
    WindowClosed,

    /// <summary>A person cancelled it.</summary>
    Cancelled,

    /// <summary>It was interrupted.</summary>
    Interrupted,

    /// <summary>Its lease lapsed.</summary>
    LeaseExpired,

    /// <summary>The engine failed.</summary>
    ExecutionFailed,

    /// <summary>A source or fetch outcome stopped it; the run records which.</summary>
    SourceOutcome,
}
