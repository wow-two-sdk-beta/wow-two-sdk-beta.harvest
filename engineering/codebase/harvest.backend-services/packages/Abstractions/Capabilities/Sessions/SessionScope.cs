namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Sessions;

/// <summary>Refers to how long an anonymous session lives.</summary>
public enum SessionScope
{
    /// <summary>One run's jar, dropped when the run ends.</summary>
    Run,

    /// <summary>A warm jar per host, reused across runs for a few hours.</summary>
    Host,
}
