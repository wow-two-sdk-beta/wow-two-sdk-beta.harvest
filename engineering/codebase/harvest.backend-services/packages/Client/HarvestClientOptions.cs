namespace WoW.Two.Sdk.Beta.Be.Harvest.Client;

/// <summary>Holds the settings of the Harvest HTTP client.</summary>
public sealed class HarvestClientOptions
{
    /// <summary>Gets or sets the absolute root URL of the running Harvest service.</summary>
    public Uri? BaseAddress { get; set; }
}
