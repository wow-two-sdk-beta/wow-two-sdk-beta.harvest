namespace Harvest.Api.Registration.Access;

/// <summary>Holds the authorization policies beside the one-per-scope policies, which take the scope's name.</summary>
public static class AccessPolicies
{
    /// <summary>Holds the policy that admits any live key; the endpoint then narrows to the caller's containers.</summary>
    public const string AnyKey = "harvest:any-key";
}
