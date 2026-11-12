namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Captcha;

/// <summary>Describes a named adapter's explicit token-task request; it carries no account cookies or headers.</summary>
public sealed record CaptchaTask
{
    /// <summary>Gets the supported task kind.</summary>
    public required CaptchaKind Kind { get; init; }
    /// <summary>Gets the source page URL; query and fragment are excluded from the provider payload.</summary>
    public required Uri WebsiteUrl { get; init; }
    /// <summary>Gets the widget's public site key.</summary>
    public required string SiteKey { get; init; }
    /// <summary>Gets whether the reCAPTCHA v2 widget is invisible.</summary>
    public bool Invisible { get; init; }
    /// <summary>Gets a Turnstile action explicitly captured by the source adapter.</summary>
    public string? Action { get; init; }
    /// <summary>Gets Turnstile challenge data explicitly supplied by the source adapter.</summary>
    public string? Data { get; init; }
    /// <summary>Gets Turnstile page data explicitly supplied by the source adapter.</summary>
    public string? PageData { get; init; }

    /// <summary>Returns the task kind without page URLs or opaque challenge data.</summary>
    public override string ToString()
    {
        return $"CAPTCHA {Kind}";
    }
}
