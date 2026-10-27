namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;

/// <summary>Refers to the kind of check a host put between a request and its content.</summary>
public enum ChallengeKind
{
    /// <summary>A JavaScript interstitial that a rendering browser clears.</summary>
    JsInterstitial,

    /// <summary>A reCAPTCHA widget.</summary>
    CaptchaRecaptcha,

    /// <summary>An hCaptcha widget.</summary>
    CaptchaHcaptcha,

    /// <summary>A Cloudflare Turnstile widget.</summary>
    CaptchaTurnstile,

    /// <summary>A GeeTest widget.</summary>
    CaptchaGeetest,

    /// <summary>A friction challenge, such as OLX's, that is not blank.</summary>
    CaptchaFriction,

    /// <summary>A captcha of another kind.</summary>
    CaptchaOther,

    /// <summary>A waiting room or queue page.</summary>
    WaitingRoom,

    /// <summary>A wall that asks to sign in.</summary>
    LoginWall,
}
