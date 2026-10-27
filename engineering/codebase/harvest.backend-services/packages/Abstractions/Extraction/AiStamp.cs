namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents the model call behind an envelope, when a model tier ran.</summary>
public sealed record AiStamp
{
    /// <summary>Gets the model.</summary>
    public required string Model { get; init; }

    /// <summary>Gets the prompt version.</summary>
    public required string PromptVersion { get; init; }

    /// <summary>Gets when the model ran (UTC).</summary>
    public required DateTimeOffset AtUtc { get; init; }

    /// <summary>Gets the tokens the call used.</summary>
    public int Tokens { get; init; }
}
