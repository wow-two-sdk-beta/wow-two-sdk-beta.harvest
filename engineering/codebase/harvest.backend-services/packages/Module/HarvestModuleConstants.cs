namespace WoW.Two.Sdk.Beta.Be.Harvest.Module;

/// <summary>Holds the names the module registers under, so a Form 1 host's own registrations never collide.</summary>
public static class HarvestModuleConstants
{
    /// <summary>Holds the module's migration set; its history lives in <c>harvest_migration_history</c>.</summary>
    public const string MigrationSetName = "harvest";

    /// <summary>Holds the key of the module's own keyed services: its Npgsql data source and connection factory. It equals
    /// <see cref="MigrationSetName"/>, because the migrator opens a set's connections from the factory keyed by its name.</summary>
    public const string ServiceKey = MigrationSetName;
}
