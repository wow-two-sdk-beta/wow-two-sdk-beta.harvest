using AwesomeAssertions;
using HarvestSources.Api.Configurations;
using Microsoft.Extensions.Configuration;
using WoW.Two.Sdk.Beta.Be.Meta;

namespace HarvestSources.Tests.E2E;

public sealed class DeploymentVerbsTests
{
    [Fact]
    public async Task Source_image_describes_its_migrations_without_target_secrets_or_startup()
    {
        var run = await Run(["migrate", "sets", "--json"], new Dictionary<string, string?>());
        run.Exit.Should().Be(0, run.Error);
        run.Output.Should().Contain("\"harvest\"").And.Contain("migrate-result ").And.NotContain("\"distributed_cache\"");
    }

    [Fact]
    public async Task Source_configuration_check_rejects_unknown_provider_settings_without_echoing_credentials()
    {
        const string credential = "synthetic-private-credential-must-not-appear";
        var run = await Run(["config", "check"], new Dictionary<string, string?>
        {
            ["DB_CONNECTION"] = "Host=127.0.0.1;Port=1;Database=unreachable;Username=synthetic;Timeout=1",
            ["HarvestInstagram:AccessToken"] = credential,
            ["HarvestInstagram:Enabeld"] = "true",
        });
        run.Exit.Should().Be(1, run.Error);
        run.Output.Should().Contain("config-result ").And.Contain("HarvestInstagram:Enabeld").And.NotContain(credential);
    }

    [Theory]
    [InlineData("geonode-uz", 0)]
    [InlineData("lease-uz", 0)]
    [InlineData("missing", 1)]
    public async Task Source_configuration_check_resolves_named_proxy_profiles(string profile, int expected)
    {
        const string credential = "synthetic-profile-secret-must-not-appear";
        var run = await Run(["config", "check"], new Dictionary<string, string?>
        {
            ["DB_CONNECTION"] = "Host=127.0.0.1;Port=1;Database=unreachable;Username=synthetic;Timeout=1",
            ["Harvest:Operator:ApiKey"] = "hv_12345678901234567890123456789012",
            ["HarvestIntegrations:Enabled"] = "true",
            ["HarvestIntegrations:OlxProxyProfile"] = profile,
            ["HarvestIntegrations:ProxyProfilesJson"] = $$$"""
                {
                  "geonode-uz": {"server":"https://proxy.example.test:443","username":"fixture","password":"{{{credential}}}"},
                  "lease-uz": {"broker":{"baseUrl":"https://broker.example.test","apiKey":"{{{credential}}}","allowedProxyHosts":["proxy.example.test"]}}
                }
                """,
        });
        run.Exit.Should().Be(expected, run.Output + run.Error);
        (run.Output + run.Error).Should().Contain("config-result ").And.NotContain(credential);
    }

    private static async Task<(int Exit, string Output, string Error)> Run(string[] args, Dictionary<string, string?> settings)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await ServiceHost.RunAsync(args,
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(settings);
                builder.Configure();
            },
            _ => throw new InvalidOperationException("Deployment commands must not start the source host."),
            null, output, error, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }
}
