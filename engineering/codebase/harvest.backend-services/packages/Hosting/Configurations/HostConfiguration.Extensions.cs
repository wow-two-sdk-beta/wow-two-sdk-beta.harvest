using Harvest.Api.Registration.Access;
using Harvest.Api.Console;
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

    /// <summary>Authenticates API keys (marker <c>hv_</c>) against the module's key table and the operator key, and
    /// registers one policy per scope.</summary>
    private static WebApplicationBuilder AddApiKeyAccess(this WebApplicationBuilder builder)
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
                gate.OpenPaths.Add("/api/console/access");
            });
        builder.Services.AddAuthorization(options =>
        {
            var scheme = builder.Configuration.GetValue<bool>(HarvestConsoleOptions.SectionName + ":IsIdentityEnabled")
                ? ConsoleAuthenticationScheme : ApiKeyAuthenticationDefaults.Scheme;
            options.AddPolicy(AccessPolicies.AnyKey, policy => policy.AddAuthenticationSchemes(scheme)
                .RequireAuthenticatedUser().AddRequirements(new HarvestAccessRequirement()));
            options.AddPolicy(AccessPolicies.TenantsRead, policy => policy.AddAuthenticationSchemes(scheme)
                .RequireAuthenticatedUser().AddRequirements(new HarvestAccessRequirement(IsTenantRead: true)));
            foreach (var scope in HarvestScopeConstants.All)
                options.AddPolicy(scope, policy => policy.AddAuthenticationSchemes(scheme)
                    .RequireAuthenticatedUser().AddRequirements(new HarvestAccessRequirement(scope)));
        });
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
