namespace WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

/// <summary>Represents a newly issued API key with its secret, which is shown this once and never stored.</summary>
public sealed record IssuedApiKeyModel
{
    /// <summary>Gets the key as the store keeps it.</summary>
    public required ApiKeyModel Key { get; init; }

    /// <summary>Gets the secret: <see cref="ApiKeyConstants.Marker"/> followed by random letters and digits.</summary>
    public required string Secret { get; init; }
}
