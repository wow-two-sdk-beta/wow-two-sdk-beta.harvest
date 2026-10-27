namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Egress;

/// <summary>Refers to whether consecutive requests keep one exit.</summary>
public enum EgressStickiness
{
    /// <summary>A new exit per request; suits single-request steps.</summary>
    Rotating,

    /// <summary>One exit for a session key: a warm cookie jar, a login, an account.</summary>
    Sticky,
}
