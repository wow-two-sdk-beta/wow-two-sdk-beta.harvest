namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Sessions;

/// <summary>Describes an existing approved account session; the application never signs in or creates accounts.</summary>
public sealed class AccountSessionOptions
{
    /// <summary>Gets or sets the non-secret account-session identifier.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Gets or sets the exact HTTPS origin, including its port.</summary>
    public string Origin { get; set; } = string.Empty;
    /// <summary>Gets or sets the operator's expiration time; expired sessions are not leased.</summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }
    /// <summary>Gets the cookies, scoped to this session's origin.</summary>
    public List<SessionCookieOptions> Cookies { get; set; } = [];
}
