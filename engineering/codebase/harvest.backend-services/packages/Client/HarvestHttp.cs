using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2ClientCredentials;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client;

/// <summary>Points every Harvest broker's HTTP client at the service root and attaches its credential: the API key
/// header, or a client-credentials token from <see cref="HarvestClientOptions.Identity"/>.</summary>
internal static class HarvestHttp
{
    /// <summary>Returns the service root with its trailing slash.</summary>
    /// <param name="baseAddress">The configured root URL.</param>
    internal static Uri Root(Uri baseAddress) =>
        new(baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress.AbsoluteUri : baseAddress.AbsoluteUri + "/");

    /// <summary>Sets the client's root and, when the client has one, its API key header.</summary>
    /// <param name="provider">The service provider.</param>
    /// <param name="http">The client being created.</param>
    internal static void Configure(IServiceProvider provider, HttpClient http)
    {
        var options = provider.GetRequiredService<IOptions<HarvestClientOptions>>().Value;
        if (options.BaseAddress is not { IsAbsoluteUri: true })
            throw new InvalidOperationException("HarvestClientOptions.BaseAddress must be an absolute URL.");
        http.BaseAddress = Root(options.BaseAddress);
        if (!string.IsNullOrEmpty(options.ApiKey))
            http.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, options.ApiKey);
    }

    /// <summary>Adds the be sdk's client-credentials handler when <c>AddHarvestClient</c> configured an Identity
    /// client; otherwise the client keeps its API key or the caller's own handler.</summary>
    /// <param name="builder">A broker's HTTP client builder.</param>
    /// <returns>The same builder.</returns>
    internal static IHttpClientBuilder WithCredentials(this IHttpClientBuilder builder)
    {
        var registration = builder.Services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(HarvestClientRegistration))
            ?.ImplementationInstance as HarvestClientRegistration;
        if (registration is not { UsesIdentity: true })
            return builder;

        builder.Services.AddOptions<OAuth2ClientCredentialsOptions>(builder.Name)
            .Configure<IOptions<HarvestClientOptions>>((target, harvest) =>
                harvest.Value.Identity!.CopyTo(target, harvest.Value.BaseAddress!));
        return builder.AddOAuth2ClientCredentials(_ => { });
    }
}

/// <summary>Records, when <c>AddHarvestClient</c> runs, whether its settings name an Identity client, so every broker
/// registered after it takes the same credential.</summary>
/// <param name="UsesIdentity">Whether the client takes client-credentials tokens.</param>
internal sealed record HarvestClientRegistration(bool UsesIdentity);
