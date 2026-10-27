namespace WoW.Two.Sdk.Beta.Be.Harvest.Client;

/// <summary>Holds the settings of the Harvest HTTP client.</summary>
public sealed class HarvestClientOptions
{
    /// <summary>Gets or sets the absolute root URL of the running Harvest service.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>Gets or sets the API key the client sends in the <c>X-Api-Key</c> header; its scopes decide what the
    /// service allows. It comes from the consumer's secret store, never from a settings file in a repository.</summary>
    public string? ApiKey { get; set; }
}
