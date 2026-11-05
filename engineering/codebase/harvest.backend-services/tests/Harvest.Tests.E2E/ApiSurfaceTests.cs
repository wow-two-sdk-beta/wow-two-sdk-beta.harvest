using System.Net;
using AwesomeAssertions;

namespace Harvest.Tests.E2E;

/// <summary>Checks that the host exposes API routes and no page fallback.</summary>
[Collection(HarvestApiCollection.Name)]
public sealed class ApiSurfaceTests(HarvestApiFixture fixture)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/some/client/route")]
    public async Task Get_ShouldReturnNotFound_ForPageRoutes(string path)
    {
        using var client = fixture.Host.CreateClient();

        using var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(HarvestApiFixture.OperatorKey, HttpStatusCode.NotFound)]
    public async Task Get_ShouldAnswerJsonNotTheAppShell_WhenAnApiRouteDoesNotExist(string? apiKey, HttpStatusCode expected)
    {
        using var client = fixture.CreateClient(apiKey);

        using var response = await client.GetAsync("/api/does-not-exist");

        response.StatusCode.Should().Be(expected);
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("<div id=\"root\">");
    }
}
