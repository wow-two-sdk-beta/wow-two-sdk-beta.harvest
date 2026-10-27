namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;

/// <summary>Represents a solved captcha's token and what it cost.</summary>
public sealed record ChallengeSolution
{
    /// <summary>Gets the token to submit.</summary>
    public required string Token { get; init; }

    /// <summary>Gets what the solve cost (USD), when the vendor reports it.</summary>
    public decimal? CostUsd { get; init; }
}
