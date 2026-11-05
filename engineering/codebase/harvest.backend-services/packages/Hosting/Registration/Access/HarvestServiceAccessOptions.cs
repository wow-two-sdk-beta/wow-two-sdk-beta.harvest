using System.Text.Json;
using System.Text.Json.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace Harvest.Api.Registration.Access;

/// <summary>Configures service tokens for the shared Harvest API.</summary>
public sealed class HarvestServiceAccessOptions
{
    internal const string SectionName = "Harvest:ServiceAccess";
    internal const string Scheme = "HarvestService";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Gets or sets whether the API requires service tokens instead of local API keys.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the exact HTTPS Identity issuer.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Gets or sets the resource audience registered for Harvest.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Gets or sets the JSON object mapping confidential client identifiers to Harvest grants.</summary>
    public string ClientGrantsJson { get; set; } = "{}";

    internal Dictionary<string, ClientGrant> ClientGrants { get; private set; } = new(StringComparer.Ordinal);

    internal void LoadGrants()
    {
        if (!Enabled || string.IsNullOrWhiteSpace(ClientGrantsJson))
            return;
        try
        {
            ClientGrants = JsonSerializer.Deserialize<Dictionary<string, ClientGrant>>(ClientGrantsJson, Json)
                ?? new(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            ClientGrants.Clear();
        }
    }

    internal bool HasValidIssuer() => Uri.TryCreate(Authority, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0
        && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.Host.Length != 0;

    internal bool HasValidGrants() => ClientGrants.Count > 0 && ClientGrants.All(entry =>
        !string.IsNullOrWhiteSpace(entry.Key) && entry.Value is { } grant
        && (grant.IsOperator ? grant.TenantId is null : grant.TenantId is { } tenant && tenant != Guid.Empty)
        && grant.Scopes is { Length: > 0 }
        && grant.Scopes.All((grant.IsOperator ? HarvestScopeConstants.All : HarvestScopeConstants.TenantScopes).Contains));

    internal sealed record ClientGrant(bool IsOperator, Guid? TenantId, string[] Scopes);
}
