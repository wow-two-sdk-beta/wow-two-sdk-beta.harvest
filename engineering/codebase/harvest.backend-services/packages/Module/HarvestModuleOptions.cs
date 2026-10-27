namespace WoW.Two.Sdk.Beta.Be.Harvest.Module;

/// <summary>Holds the settings of the in-process Harvest module.</summary>
public sealed class HarvestModuleOptions
{
    /// <summary>Gets or sets the PostgreSQL connection string of the database that holds the Harvest tables; the
    /// database must use UTF-8 and offer <c>pg_trgm</c>.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets the operator API key — a Harvest-shaped secret that holds every scope and belongs to no
    /// tenant; operator access is off when it is empty. It comes from a host file or the vault, never from a settings
    /// file in the repository.</summary>
    public string? OperatorApiKey { get; set; }
}
