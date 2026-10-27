using Harvest.Api.Registration.Access;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Web.Json;

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
                // Every /api call presents a key, this machine included; the status probe stays open.
                gate.AllowLocalWithoutKey = false;
                gate.OpenPaths.Add("/api/system/status");
            });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AccessPolicies.AnyKey, policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationDefaults.Scheme)
                .RequireAuthenticatedUser());
            foreach (var scope in HarvestScopeConstants.All)
                options.AddPolicy(scope, policy => policy.RequireApiKeyScope(scope));
        });
        builder.Services.AddScoped<CallerResolver>();
        return builder;
    }

    /// <summary>Registers controllers on the SDK wire contract.</summary>
    private static WebApplicationBuilder AddControllers(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllersWithSdkJson();
        return builder;
    }

    /// <summary>Resolves the connection string: the <c>DB_CONNECTION</c> alias wins over <c>DatabaseSettings:ConnectionString</c>.</summary>
    private static string ResolveConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration["DB_CONNECTION"];
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = configuration["DatabaseSettings:ConnectionString"];

        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException("Database connection string not found. Set DB_CONNECTION or DatabaseSettings:ConnectionString.")
            : connectionString;
    }
}
