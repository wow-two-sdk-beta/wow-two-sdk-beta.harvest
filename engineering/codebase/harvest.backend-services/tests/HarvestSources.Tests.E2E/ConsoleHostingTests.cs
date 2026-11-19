using System.Net;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace HarvestSources.Tests.E2E;

/// <summary>
/// The image's one origin: the console from <c>wwwroot</c> at <c>/</c>, every client route falling back to its
/// <c>index.html</c> so a deep link reloads, and <c>/api</c>, reserved paths and missing files never answering the page.
/// The web root holds a stand-in bundle, as the image's console stage writes one.
/// </summary>
public sealed class ConsoleHostingTests : IAsyncLifetime
{
    private const string OperatorKey = "hv_ConsoleHostingTestOperator000000";
    private const string Shell = "<div id=\"root\"></div>";
    private const string Asset = "/assets/index-Bx3kQ9.js";
    private readonly string _webRoot = Directory.CreateTempSubdirectory("harvest-console-").FullName;
    private WebApplication _host = null!;

    public async Task InitializeAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "index.html"), $"<!doctype html><html><body>{Shell}</body></html>");
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "assets", Path.GetFileName(Asset)), "export {};");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Development",
            WebRootPath = _webRoot,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseSettings:ConnectionString"] = await SourcePostgres.NewDatabaseAsync(),
            ["Harvest:Operator:ApiKey"] = OperatorKey,
            ["Harvest:Worker:Enabled"] = "false",
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
        });
        builder.WebHost.UseTestServer();
        _host = builder.Configure().Build();
        await _host.ConfigureAsync();
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        Directory.Delete(_webRoot, recursive: true);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/sources")]
    [InlineData("/sources/0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5c/posts/0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5d")]
    [InlineData("/pipelines?tab=runs")]
    public async Task Get_ShouldServeTheConsoleShell_ForPagesAndDeepLinks(string path)
    {
        using HttpClient client = _host.GetTestClient();

        using HttpResponseMessage response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        response.Headers.CacheControl?.NoCache.Should().BeTrue("a deploy must reach a returning browser");
        response.Headers.Contains("Cross-Origin-Embedder-Policy").Should().BeFalse("source images load from their own hosts");
        (await response.Content.ReadAsStringAsync()).Should().Contain(Shell);
    }

    [Fact]
    public async Task Get_ShouldServeHashedAssets_AsImmutable()
    {
        using HttpClient client = _host.GetTestClient();

        using HttpResponseMessage response = await client.GetAsync(Asset);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/javascript");
        response.Headers.CacheControl?.ToString().Should().Contain("immutable");
    }

    [Theory]
    [InlineData("/api/system/status", null, HttpStatusCode.OK)]
    [InlineData("/api/does-not-exist", null, HttpStatusCode.Unauthorized)]
    [InlineData("/api/does-not-exist", OperatorKey, HttpStatusCode.NotFound)]
    [InlineData("/api/sources/0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5c/no-such-tab", OperatorKey, HttpStatusCode.NotFound)]
    [InlineData("/api", OperatorKey, HttpStatusCode.NotFound)]
    [InlineData("/.well-known/openid-configuration", null, HttpStatusCode.NotFound)]
    [InlineData("/assets/index-missing.js", null, HttpStatusCode.NotFound)]
    public async Task Get_ShouldNeverAnswerTheConsoleShell_ForApiRoutesReservedPathsAndMissingFiles(
        string path, string? apiKey, HttpStatusCode expected)
    {
        using HttpClient client = _host.GetTestClient();
        if (apiKey is not null)
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        using HttpResponseMessage response = await client.GetAsync(path);

        response.StatusCode.Should().Be(expected);
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html");
        (await response.Content.ReadAsStringAsync()).Should().NotContain(Shell);
    }
}
