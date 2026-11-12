namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Captcha;

/// <summary>Identifies the explicitly supported token-task formats.</summary>
public enum CaptchaKind
{
    /// <summary>A reCAPTCHA v2 token task.</summary>
    RecaptchaV2,
    /// <summary>A Cloudflare Turnstile token task.</summary>
    Turnstile,
}
