namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Captcha;

/// <summary>Configures 2Captcha's JSON v2 protocol; no task is submitted until explicitly enabled and called.</summary>
public sealed class TwoCaptchaOptions
{
    /// <summary>Gets or sets whether submission is allowed; disabled by default.</summary>
    public bool Enabled { get; set; }
    /// <summary>Gets or sets the provider key from a private host file or vault.</summary>
    public string? ApiKey { get; set; }
    /// <summary>Gets or sets the exact HTTPS API base.</summary>
    public string BaseUrl { get; set; } = "https://api.2captcha.com/";
    /// <summary>Gets the exact source origins that may be submitted to the provider.</summary>
    public List<string> AllowedOrigins { get; set; } = [];
    /// <summary>Gets or sets the interval between result requests; the provider requires at least five seconds.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets or sets the maximum total solve time.</summary>
    public TimeSpan SolveTimeout { get; set; } = TimeSpan.FromMinutes(2);
    /// <summary>Gets or sets the finite maximum number of result requests per submission.</summary>
    public int MaximumPolls { get; set; } = 24;
}
