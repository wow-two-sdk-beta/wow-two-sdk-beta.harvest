namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents who published a post; a phone number given as a name moves to the contacts.</summary>
public sealed record PostAuthor
{
    /// <summary>Gets the author's name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the author's handle on the source.</summary>
    public string? Handle { get; init; }

    /// <summary>Gets the author's profile URL.</summary>
    public string? Url { get; init; }
}
