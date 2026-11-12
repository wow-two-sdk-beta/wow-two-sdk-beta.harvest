namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Captcha;

/// <summary>Describes a submitted task or its result without echoing provider keys or unsanitized diagnostics.</summary>
public sealed record CaptchaTaskResult
{
    /// <summary>Gets the provider's task ID after a successful submission.</summary>
    public long? TaskId { get; init; }
    /// <summary>Gets submitted, processing, ready, disabled, unavailable, failed, or timeout.</summary>
    public required string Status { get; init; }
    /// <summary>Gets the solved token; only a source-specific broker knows how to apply it.</summary>
    public string? Token { get; init; }
    /// <summary>Gets the returned task-specific user agent, when supplied.</summary>
    public string? UserAgent { get; init; }
    /// <summary>Gets the provider-reported price; unknown remains null.</summary>
    public decimal? CostUsd { get; init; }
    /// <summary>Gets a bounded provider or local error code, never raw response text.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Returns non-secret task diagnostics, excluding the token.</summary>
    public override string ToString()
    {
        return $"CAPTCHA {TaskId}: {Status} ({ErrorCode})";
    }
}
