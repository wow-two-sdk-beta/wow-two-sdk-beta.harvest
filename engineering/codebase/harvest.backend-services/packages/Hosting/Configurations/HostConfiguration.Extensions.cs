using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Web.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace Harvest.Api.Configurations;

public static partial class HostConfiguration
{
    private const string ApiAuthenticationScheme = "HarvestApi";

    /// <summary>Holds the configuration key of the operator API key, set from a host file or the vault.</summary>
    public const string OperatorApiKeySetting = "Harvest:Operator:ApiKey";

    /// <summary>Hosts the Form 1 module over the service's PostgreSQL database, with its migration set and self-test.</summary>
    private static WebApplicationBuilder AddHarvestModule(this WebApplicationBuilder builder)
    {
        // Resolved lazily, so a test host's configuration overrides still reach the connection string and the key.
        builder.Services.AddHarvest().Configure<IConfiguration>((options, configuration) =>
        {
            options.ConnectionString = ResolveConnectionString(configuration);
            options.OperatorApiKey = configuration[OperatorApiKeySetting];
        });
        builder.Services.AddHarvestFetching();
        builder.Services.AddValidatedSettings<HarvestFetchOptions>(builder.Configuration, "Harvest:Fetch", options => options
            .Validate(value => value.Timeout > TimeSpan.Zero && value.MaximumResponseBytes > 0 && value.MaximumRedirects >= 0,
                "Harvest:Fetch requires a positive timeout and response limit, and a nonnegative redirect limit."));
        builder.Services.AddHarvestReaders();
        builder.Services.AddHarvestExtraction();
        builder.Services.AddHarvestPosts();
        builder.Services.AddHarvestDelivery();
        builder.Services.AddHarvestEngine();
        builder.Services.AddValidatedSettings<HarvestEngineOptions>(builder.Configuration, "Harvest:Worker", options => options
            .Validate(value => value.LeaseDuration > TimeSpan.Zero && value.PollInterval > TimeSpan.Zero
                && value.MaxItems > 0 && value.MaxRequests > 0 && value.MaxBytes > 0 && value.MaxDuration > TimeSpan.Zero
                && value.RefusalBackoff > TimeSpan.Zero, "Harvest:Worker limits and intervals must be positive."));
        builder.Services.AddHarvestWorker();
        return builder;
    }

    private static WebApplicationBuilder AddHarvestAccess(this WebApplicationBuilder builder)
    {
        builder.Services.AddHarvestApiKeys();
        builder.Services.AddApiKeyAuthentication(
            keys =>
            {
                keys.Marker = ApiKeyConstants.Marker;
                keys.RandomLength = ApiKeyConstants.RandomLength;
            },
            gate =>
            {
                // Key-only mode requires a key on protected /api calls, this machine included.
                gate.IsLocalAllowedWithoutKey = false;
                gate.OpenPaths.Add("/api/system/status");
            });
        builder.Services.AddValidatedSettings<HarvestServiceAccessOptions>(builder.Configuration,
            HarvestServiceAccessOptions.SectionName, rules => rules
                .Validate(options => !options.Enabled || options.HasValidIssuer() && !string.IsNullOrWhiteSpace(options.Audience),
                    "Harvest:ServiceAccess requires an HTTPS issuer and a resource audience.")
                .Validate(options => !options.Enabled || options.HasValidGrants(),
                    "Harvest:ServiceAccess:ClientGrantsJson must bind clients to valid tenant or operator scopes."));
        builder.Services.PostConfigure<HarvestServiceAccessOptions>(options => options.LoadGrants());
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = ApiAuthenticationScheme;
            options.DefaultAuthenticateScheme = ApiAuthenticationScheme;
            options.DefaultChallengeScheme = ApiAuthenticationScheme;
            options.DefaultForbidScheme = ApiAuthenticationScheme;
        }).AddPolicyScheme(ApiAuthenticationScheme, null, options => options.ForwardDefaultSelector = context =>
            context.RequestServices.GetRequiredService<IOptions<HarvestServiceAccessOptions>>().Value.Enabled
                ? HarvestServiceAccessOptions.Scheme : ApiKeyAuthenticationDefaults.Scheme)
            .AddJwtBearer(HarvestServiceAccessOptions.Scheme, _ => { });
        builder.Services.AddOptions<JwtBearerOptions>(HarvestServiceAccessOptions.Scheme)
            .Configure<IOptions<HarvestServiceAccessOptions>>((options, access) =>
            {
                options.Authority = access.Value.Authority;
                options.Audience = access.Value.Audience;
                options.MapInboundClaims = false;
                options.SaveToken = false;
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters.AuthenticationType = HarvestServiceAccessOptions.Scheme;
                options.TokenValidationParameters.ValidTypes = ["at+jwt", "application/at+jwt"];
                options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
                options.TokenValidationParameters.IgnoreTrailingSlashWhenValidatingAudience = false;
            });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AccessPolicies.AnyCaller, policy => policy.AddAuthenticationSchemes(ApiAuthenticationScheme)
                .RequireAuthenticatedUser().AddRequirements(new HarvestAccessRequirement()));
            foreach (var scope in HarvestScopeConstants.All)
                options.AddPolicy(scope, policy => policy.AddAuthenticationSchemes(ApiAuthenticationScheme)
                    .RequireAuthenticatedUser().AddRequirements(new HarvestAccessRequirement(scope)));
        });
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IAuthorizationHandler, HarvestAccessAuthorizationHandler>();
        builder.Services.AddScoped<CallerResolver>();
        builder.Services.AddScoped<Harvest.Api.Registration.SourceCapabilitiesResolver>();
        return builder;
    }

    /// <summary>Registers controllers on the SDK wire contract.</summary>
    private static WebApplicationBuilder AddControllers(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllersWithSdkJson()
            .AddApplicationPart(typeof(HostConfiguration).Assembly);
        return builder;
    }

    /// <summary>Resolves the connection string: the <c>DB_CONNECTION</c> alias wins over <c>DatabaseSettings:ConnectionString</c>.</summary>
    private static string ResolveConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration["DB_CONNECTION"];
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = configuration["DatabaseSettings:ConnectionString"];

        return connectionString ?? string.Empty;
    }
}
