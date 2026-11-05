namespace Harvest.Api.Registration.Access;

/// <summary>Holds the authorization policies beside the one-per-scope policies, which take the scope's name.</summary>
public static class AccessPolicies
{
    /// <summary>Holds the policy for an authenticated Harvest caller.</summary>
    public const string AnyCaller = "harvest:any-caller";
}
