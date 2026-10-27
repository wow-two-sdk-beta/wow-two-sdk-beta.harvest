namespace WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

/// <summary>Refers to whether an API key still authenticates.</summary>
public enum ApiKeyState
{
    /// <summary>The key authenticates until it expires.</summary>
    Active,

    /// <summary>The key was revoked and never authenticates again.</summary>
    Revoked,
}
