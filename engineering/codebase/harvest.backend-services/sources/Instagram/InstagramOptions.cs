namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Runtime-only Meta credentials and recent-media bounds. Never stored in pipeline scope or captures.</summary>
public sealed class InstagramOptions
{
    /// <summary>Gets or sets explicit activation; disabled by default.</summary>
    public bool Enabled { get; set; }
    /// <summary>Gets or sets the operator's Facebook Login access token with approved Instagram access.</summary>
    public string AccessToken { get; set; } = string.Empty;
    /// <summary>Gets or sets the operator's Page-linked professional Instagram user ID.</summary>
    public string ProfessionalAccountId { get; set; } = string.Empty;
    /// <summary>Gets or sets the supported Graph API version, including the v prefix. No version is inferred.</summary>
    public string ApiVersion { get; set; } = string.Empty;
    /// <summary>Gets or sets the exact publisher usernames approved for collection. Empty allows no accounts.</summary>
    public string[] AllowedUsernames { get; set; } = [];
    /// <summary>Gets or sets media items requested per page, from 1 to 50.</summary>
    public int PageSize { get; set; } = 25;
    /// <summary>Gets or sets the maximum recent pages per run, from 1 to 20.</summary>
    public int MaximumPages { get; set; } = 4;
    /// <summary>Gets or sets the maximum response bytes per request, from 1 KiB to 8 MiB.</summary>
    public int MaximumResponseBytes { get; set; } = 2_000_000;
    /// <summary>Gets or sets the per-request deadline, no more than one minute.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
