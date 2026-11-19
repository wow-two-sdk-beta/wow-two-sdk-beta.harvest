using System.Net;
using AwesomeAssertions;

namespace Harvest.Tests.E2E;

/// <summary>Checks that an unmatched API route answers the API's own status, never the console's page; the console's
/// routes and its fallback are HarvestSources.Tests.E2E's <c>ConsoleHostingTests</c>.</summary>
[Collection(HarvestApiCollection.Name)]
public sealed class ApiSurfaceTests(HarvestApiFixture fixture)
{
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
