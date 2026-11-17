namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Host-only Meta connection settings. Collection policy belongs to the persisted source and pipelines.</summary>
public sealed class InstagramOptions
{
    /// <summary>Gets or sets the approved Graph API origin. Only HTTPS graph.facebook.com is accepted.</summary>
    public string BaseAddress { get; set; } = "https://graph.facebook.com/";
    /// <summary>Gets or sets the operator's Facebook Login access token with approved Instagram access.</summary>
    public string AccessToken { get; set; } = string.Empty;
    /// <summary>Gets or sets the operator's Page-linked professional Instagram user ID.</summary>
    public string ProfessionalAccountId { get; set; } = string.Empty;
    /// <summary>Gets or sets the supported Graph API version, including the v prefix. No version is inferred.</summary>
    public string ApiVersion { get; set; } = string.Empty;
}
