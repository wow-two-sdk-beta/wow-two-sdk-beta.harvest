namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Refers to whether fetches of a source obey the host's <c>robots.txt</c>.</summary>
public enum RobotsPolicy
{
    /// <summary>A disallowed path is an outcome, never fetched; the default.</summary>
    Obey,

    /// <summary>The operator chose to read the source whatever <c>robots.txt</c> says.</summary>
    Ignore,
}
