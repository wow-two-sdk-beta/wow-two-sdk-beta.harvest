namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Describes local adapter configuration, not a promise of remote access.</summary>
public enum SourceReadiness
{
    /// <summary>The implementation and its required local settings are available.</summary>
    Ready,
    /// <summary>The operator must supply or enable local settings.</summary>
    ConfigurationRequired,
    /// <summary>No usable implementation is installed.</summary>
    Unavailable,
}
