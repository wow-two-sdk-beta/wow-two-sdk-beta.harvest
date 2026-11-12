namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Captcha;

/// <summary>Submits and polls one explicitly requested CAPTCHA task; no host refusal invokes it automatically.</summary>
public interface ICaptchaSolver
{
    /// <summary>Submits at most one paid provider task.</summary>
    Task<CaptchaTaskResult> SubmitAsync(CaptchaTask task, CancellationToken cancellationToken = default);
    /// <summary>Reads a previously submitted provider task.</summary>
    Task<CaptchaTaskResult> GetResultAsync(long taskId, CancellationToken cancellationToken = default);
    /// <summary>Submits once and polls within the configured time and attempt limits.</summary>
    Task<CaptchaTaskResult> SolveAsync(CaptchaTask task, CancellationToken cancellationToken = default);
}
