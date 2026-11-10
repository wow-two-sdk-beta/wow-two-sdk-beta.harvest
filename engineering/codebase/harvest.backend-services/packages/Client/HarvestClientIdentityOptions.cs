using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client;

/// <summary>Holds the Identity client that the Harvest client takes its service tokens from, using the be sdk's own
/// OAuth2 settings: the token endpoint, the client id, <see cref="OAuth2TokenClientOptions.ClientSecret"/> or
/// <see cref="OAuth2TokenClientOptions.ClientPrivateKey"/>, the resource and the scopes, such as
/// <c>harvest.events-read</c>.</summary>
/// <remarks>The client acts as itself with <c>client_credentials</c>. A null
/// <see cref="OAuth2TokenClientOptions.Resource"/> requests <see cref="HarvestClientOptions.BaseAddress"/> with its
/// trailing slash, which is the audience SCFD accepts. The be sdk handler fetches, caches and renews every token.</remarks>
public sealed record HarvestClientIdentityOptions : OAuth2TokenClientOptions
{
    /// <summary>Copies these settings onto a be sdk grant's options, the resource defaulting to the service root.</summary>
    /// <param name="target">The grant's options.</param>
    /// <param name="baseAddress">The service root.</param>
    internal void CopyTo(OAuth2TokenClientOptions target, Uri baseAddress)
    {
        target.TokenEndpoint = TokenEndpoint;
        target.ClientId = ClientId;
        target.ClientSecret = ClientSecret;
        target.ClientPrivateKey = ClientPrivateKey;
        target.ClientPrivateKeyId = ClientPrivateKeyId;
        target.ClientAssertionAudience = ClientAssertionAudience;
        target.Resource = string.IsNullOrWhiteSpace(Resource) ? HarvestHttp.Root(baseAddress).AbsoluteUri : Resource;
        target.Scope = Scope;
        target.RefreshSkew = RefreshSkew;
        foreach (var parameter in AdditionalParameters)
            target.AdditionalParameters[parameter.Key] = parameter.Value;
    }
}
