namespace WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

/// <summary>Holds the shape of Harvest API key secrets.</summary>
public static class ApiKeyConstants
{
    /// <summary>Holds the marker every Harvest secret starts with, so a leaked key is recognizable.</summary>
    public const string Marker = "hv_";

    /// <summary>Holds how many random letters and digits follow the marker.</summary>
    public const int RandomLength = 32;

    /// <summary>Holds the identifier the configured operator key authenticates as; it belongs to no tenant.</summary>
    public const string OperatorKeyId = "operator";

    /// <summary>Holds the longest key name.</summary>
    public const int MaxNameLength = 120;
}
