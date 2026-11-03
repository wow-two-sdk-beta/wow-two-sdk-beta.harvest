using Harvest.Api.Console;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using WoW.Two.Sdk.Beta.Be.Caching.Postgres;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Identity.Bff;

namespace Harvest.Api.Configurations;

public static partial class HostConfiguration
{
    private const string ConsoleAuthenticationScheme = "HarvestConsole";

    private static WebApplicationBuilder AddConsoleIdentity(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<HarvestConsoleOptions>()
            .Bind(builder.Configuration.GetSection(HarvestConsoleOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.PermissionNamespace) && !options.PermissionNamespace.Contains(':'),
                "Harvest:Console:PermissionNamespace must be a nonempty application namespace without ':'.")
            .Validate(options => options.OrganizationTenants.All(binding => binding.Key != Guid.Empty && binding.Value != Guid.Empty),
                "Harvest:Console:OrganizationTenants must bind nonempty organization and tenant identifiers.")
            .ValidateOnStart();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IAuthorizationHandler, HarvestAccessAuthorizationHandler>();
        if (!builder.Configuration.GetValue<bool>(HarvestConsoleOptions.SectionName + ":IsIdentityEnabled"))
            return builder;

        builder.Services.AddHarvestConsoleIdentity(builder.Configuration);
        return builder;
    }

    /// <summary>Registers Harvest's Identity browser session, durable cache and credential-selecting authentication scheme.</summary>
    /// <param name="services">The host services.</param>
    /// <param name="configuration">The Identity client, database and persistent key settings.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddHarvestConsoleIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddIdentityBff();
        services.AddPostgresDistributedCache();
        services.AddOptions<PostgresDistributedCacheOptions>().Configure<IConfiguration>((options, configuration) =>
            options.ConnectionString = ResolveConnectionString(configuration));

        var keysPath = configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(keysPath))
            throw new InvalidOperationException("DataProtection:KeysPath is required for Harvest Identity sessions.");
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .SetApplicationName("harvest-" + configuration["Identity:Client:ClientId"]);

        // A presented service credential takes precedence over a cookie, including when that key is invalid.
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = ConsoleAuthenticationScheme;
            options.DefaultAuthenticateScheme = ConsoleAuthenticationScheme;
            options.DefaultChallengeScheme = ConsoleAuthenticationScheme;
            options.DefaultForbidScheme = ConsoleAuthenticationScheme;
        }).AddPolicyScheme(ConsoleAuthenticationScheme, null, options => options.ForwardDefaultSelector = context =>
            context.Request.Headers.ContainsKey(ApiKeyAuthenticationDefaults.HeaderName)
                || context.Request.Headers.ContainsKey("Authorization")
                ? ApiKeyAuthenticationDefaults.Scheme
                : BffAuthenticationSchemeConstants.Cookie);
        return services;
    }
}
