namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents a contact point a post gives; personal data, kept out of pool reads and keyed by HMAC for
/// matching.</summary>
public sealed record PostContact
{
    /// <summary>Gets the kind of contact point.</summary>
    public required ContactKind Kind { get; init; }

    /// <summary>Gets the normalized value.</summary>
    public required string Value { get; init; }

    /// <summary>Gets the text it was read from.</summary>
    public string? Raw { get; init; }
}
