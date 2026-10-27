namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Configures direct fetching without any external transport integration.</summary>
public sealed class HarvestFetchOptions
{
    /// <summary>Gets or sets the transparent user agent; it must identify Harvest.</summary>
    public string UserAgent { get; set; } = "Harvest/0.1";

    /// <summary>Gets or sets the minimum host gap; registrations require at least two seconds.</summary>
    public TimeSpan MinimumDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets or sets the deadline, including robots and pacing waits.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets or sets the maximum uncompressed response size.</summary>
    public int MaximumResponseBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Gets or sets the maximum same-host redirects; each hop uses the guarded transport.</summary>
    public int MaximumRedirects { get; set; } = 5;

    /// <summary>Gets or sets how long a successful robots response is cached.</summary>
    public TimeSpan RobotsCacheDuration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Gets or sets the first refusal pause; repeated refusals widen it, up to one hour.</summary>
    public TimeSpan RefusalBackoff { get; set; } = TimeSpan.FromMinutes(5);
}
