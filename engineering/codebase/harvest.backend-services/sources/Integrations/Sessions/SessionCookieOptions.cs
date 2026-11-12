namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Sessions;

/// <summary>Holds one operator-supplied cookie; its origin is supplied by the account session.</summary>
public sealed class SessionCookieOptions
{
    /// <summary>Gets or sets the cookie's name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the secret value from a private host file or vault.</summary>
    public string Value { get; set; } = string.Empty;
    /// <summary>Gets or sets its path within the exact source origin.</summary>
    public string Path { get; set; } = "/";
}
