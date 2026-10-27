namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Holds worker lease, idle polling and finite run defaults.</summary>
public sealed class HarvestEngineOptions
{
    /// <summary>Gets or sets whether the explicitly registered background worker processes schedules.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Gets or sets the claim lifetime; expired work is reclaimed after a worker dies.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(3);
    /// <summary>Gets or sets the delay between idle queue scans.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>Gets or sets the default item limit for a run.</summary>
    public int MaxItems { get; set; } = 2000;
    /// <summary>Gets or sets the default request limit for a run.</summary>
    public int MaxRequests { get; set; } = 5000;
    /// <summary>Gets or sets the default maximum byte count for a run.</summary>
    public long MaxBytes { get; set; } = 100_000_000;
    /// <summary>Gets or sets the default maximum runtime.</summary>
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromHours(1);
    /// <summary>Gets or sets the initial host pause following a refusal.</summary>
    public TimeSpan RefusalBackoff { get; set; } = TimeSpan.FromMinutes(15);
}
