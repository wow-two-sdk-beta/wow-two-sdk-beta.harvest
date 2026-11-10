namespace Harvest.Api.Registration.Access;

/// <summary>Holds the authorization policies beside the one-per-scope policies, which take the scope's name.</summary>
public static class AccessPolicies
{
    /// <summary>Holds the policy for an authenticated Harvest caller.</summary>
    public const string AnyCaller = "harvest:any-caller";

    /// <summary>Holds the policy for operators and sessions reading their own tenant.</summary>
    public const string TenantsRead = "harvest:tenants-read";

    /// <summary>Holds the operator policy for writing captures and reconciling stored posts.</summary>
    public const string ManageStorage = "harvest:manage-storage";

    /// <summary>Holds the operator policy for the console's source writes: groups, names and reading pace.</summary>
    public const string ManageSources = "harvest:manage-sources";
}
