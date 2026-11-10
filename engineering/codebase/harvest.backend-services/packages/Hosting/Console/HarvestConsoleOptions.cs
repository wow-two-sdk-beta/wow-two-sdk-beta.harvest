namespace Harvest.Api.Console;

/// <summary>Configures Identity sign-in and the accounts and organizations authorized to use SCFD.</summary>
public sealed class HarvestConsoleOptions
{
    /// <summary>Holds the host configuration section.</summary>
    public const string SectionName = "Harvest:Console";

    /// <summary>Gets or sets whether the console uses Identity sessions.</summary>
    public bool IsIdentityEnabled { get; set; }

    /// <summary>Gets or sets the namespace for the application's Identity permissions.</summary>
    public string PermissionNamespace { get; set; } = "harvest";

    /// <summary>Gets or sets the trusted issuer's subject IDs that act as SCFD platform operators.</summary>
    public string[] OperatorSubjectIds { get; set; } = [];

    /// <summary>Gets or sets the bindings from Identity organizations to SCFD tenants.</summary>
    public Dictionary<string, Guid> OrganizationTenants { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
