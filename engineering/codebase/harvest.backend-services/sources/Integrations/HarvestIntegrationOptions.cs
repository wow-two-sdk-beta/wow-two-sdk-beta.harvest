using System.Text.Json;
using System.Text.Json.Serialization;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Captcha;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Proxy;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Routing;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Sessions;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations;

/// <summary>Opts explicitly configured private-source integrations into a host; all providers start disabled.</summary>
public sealed class HarvestIntegrationOptions
{
    private static readonly JsonSerializerOptions ProfileJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    /// <summary>Gets or sets whether configured transport routes and sessions may be used.</summary>
    public bool Enabled { get; set; }
    /// <summary>Gets or sets the optional fixed proxy used for OLX HTTPS requests.</summary>
    public ProxyConnectionOptions? OlxProxy { get; set; }
    /// <summary>Gets or sets the name of the configured profile for OLX HTTPS requests.</summary>
    public string? OlxProxyProfile { get; set; }
    /// <summary>Gets or sets the vault-provided JSON object of named SDK proxy profiles.</summary>
    public string? ProxyProfilesJson { get; set; }
    /// <summary>Gets the exact-origin transport routes; no matching entry means direct fetching.</summary>
    public List<FetchRouteOptions> Routes { get; set; } = [];
    /// <summary>Gets operator-supplied account sessions; no login or account creation is performed.</summary>
    public List<AccountSessionOptions> Sessions { get; set; } = [];
    /// <summary>Gets the separately enabled CAPTCHA provider settings.</summary>
    public TwoCaptchaOptions Captcha { get; set; } = new();

    internal IReadOnlyDictionary<string, OutboundProxyProfileOptions>? ProxyProfiles { get; private set; } =
        new Dictionary<string, OutboundProxyProfileOptions>(StringComparer.Ordinal);

    internal void LoadProxyProfiles()
    {
        if (string.IsNullOrWhiteSpace(ProxyProfilesJson))
        {
            ProxyProfiles = new Dictionary<string, OutboundProxyProfileOptions>(StringComparer.Ordinal);
            return;
        }

        try
        {
            ProxyProfiles = JsonSerializer.Deserialize<Dictionary<string, OutboundProxyProfileOptions>>(ProxyProfilesJson, ProfileJson);
        }
        catch (JsonException)
        {
            ProxyProfiles = null;
        }
    }

    internal bool HasValidProxyConfiguration()
    {
        if (ProxyProfiles is not { } profiles
            || profiles.Any(pair => !IsProfileName(pair.Key) || pair.Value?.IsValid() != true))
            return false;

        bool hasInlineOlx = !string.IsNullOrWhiteSpace(OlxProxy?.Server);
        if (hasInlineOlx && (!Enabled || !SafeOutboundProxy.TryParseEndpoint(OlxProxy!.Server, out _)))
            return false;
        if (!string.IsNullOrWhiteSpace(OlxProxyProfile)
            && (!Enabled || hasInlineOlx || !profiles.ContainsKey(OlxProxyProfile)))
            return false;

        return Routes is not null && Routes.All(route => route is not null
            && (string.IsNullOrWhiteSpace(route.ProxyProfile)
                || route.Mode == FetchRouteMode.Proxy && route.Proxy is null && route.IpLease is null
                && profiles.ContainsKey(route.ProxyProfile)));
    }

    private static bool IsProfileName(string name) => name is { Length: > 0 and <= 80 }
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
