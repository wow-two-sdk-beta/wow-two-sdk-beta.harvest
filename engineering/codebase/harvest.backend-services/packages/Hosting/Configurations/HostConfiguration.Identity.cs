using Harvest.Api.Console;
using Microsoft.AspNetCore.DataProtection;
using WoW.Two.Sdk.Beta.Be.Caching.Postgres;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Bff;

namespace Harvest.Api.Configurations;

public static partial class HostConfiguration
{
    private static WebApplicationBuilder AddConsoleIdentity(this WebApplicationBuilder builder)
    {
        builder.Services.AddValidatedSettings<HarvestConsoleOptions>(builder.Configuration, HarvestConsoleOptions.SectionName, rules => rules
            .Validate(options => !string.IsNullOrWhiteSpace(options.PermissionNamespace) && !options.PermissionNamespace.Contains(':'),
                "Harvest:Console:PermissionNamespace must be a nonempty namespace without ':'.")
            .Validate(options => options.OperatorSubjectIds.All(id => !string.IsNullOrWhiteSpace(id) && id == id.Trim()),
                "Harvest:Console:OperatorSubjectIds must contain nonempty exact Identity subjects.")
            .Validate(options => options.OrganizationTenants.All(binding => Guid.TryParseExact(binding.Key, "D", out var organization)
                && organization != Guid.Empty && binding.Value != Guid.Empty),
                "Harvest:Console:OrganizationTenants must bind nonempty organization and tenant identifiers."));
        if (!builder.Configuration.GetValue<bool>(HarvestConsoleOptions.SectionName + ":IsIdentityEnabled"))
            return builder;

        var keysPath = builder.Configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(keysPath))
            throw new InvalidOperationException("DataProtection:KeysPath is required for SCFD Identity sessions.");
        builder.Services.AddIdentityBff();
        builder.Services.AddPostgresDistributedCache();
        builder.Services.AddOptions<PostgresDistributedCacheOptions>().Configure<IConfiguration>((options, configuration) =>
            options.ConnectionString = ResolveConnectionString(configuration));
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .SetApplicationName("harvest-" + builder.Configuration["Identity:Client:ClientId"]);
        return builder;
    }
}
