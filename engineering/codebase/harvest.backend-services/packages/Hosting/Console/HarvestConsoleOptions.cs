namespace Harvest.Api.Console;

/// <summary>Configures browser sign-in and the explicit bindings between Identity organizations and Harvest tenants.</summary>
public sealed class HarvestConsoleOptions
{
    /// <summary>Holds the host configuration section.</summary>
    public const string SectionName = "Harvest:Console";

    /// <summary>Gets or sets whether the console signs people in through the Identity BFF.</summary>
    public bool IsIdentityEnabled { get; set; }

    /// <summary>Gets or sets the application namespace of permissions, such as <c>harvest:pipelines:read</c>.</summary>
    public string PermissionNamespace { get; set; } = "harvest";

    /// <summary>Gets or sets the organization-to-tenant bindings; membership alone grants no access to an unbound tenant.</summary>
    public Dictionary<Guid, Guid> OrganizationTenants { get; set; } = [];
}
