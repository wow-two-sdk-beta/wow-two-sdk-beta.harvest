namespace WoW.Two.Sdk.Beta.Be.Harvest.Registration;

/// <summary>Holds the limits shared by every registration request.</summary>
public static class RegistrationConstants
{
    /// <summary>Holds the longest tenant slug.</summary>
    public const int MaxSlugLength = 63;

    /// <summary>Holds the longest display name.</summary>
    public const int MaxNameLength = 200;

    /// <summary>Holds the longest external reference.</summary>
    public const int MaxExternalRefLength = 200;
}
