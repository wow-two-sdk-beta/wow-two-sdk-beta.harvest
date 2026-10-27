namespace WoW.Two.Sdk.Beta.Be.Harvest.Module;

/// <summary>Holds the settings of the in-process Harvest module.</summary>
public sealed class HarvestModuleOptions
{
    /// <summary>Gets or sets the SQLite connection string of the store that holds the Harvest tables.</summary>
    public string ConnectionString { get; set; } = string.Empty;
}
