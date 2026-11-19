using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Client;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Entities;

namespace Harvest.Tests.Unit.Client;

/// <summary>The entity client's routes and its mapping of the service's answers; the happy path is conformance's.</summary>
public sealed class ApiEntitiesBrokerTests
{
    private static readonly Guid Container = Guid.Parse("0190a1b2-0000-7000-8000-000000000001");

    [Fact]
    public async Task Requests_ShouldNameTheContainerAndEscapeEveryParameter()
    {
        var entity = Guid.NewGuid();
        var (broker, handler) = Broker(new StubHandler(HttpStatusCode.OK, """{"data":{"data":[],"next":"","hasMore":false}}"""));

        await broker.SearchAsync(Container, "Неделя моды & co", "event", "style-fashion", 5);
        handler.Paths[^1].Should().Be($"/api/entities?containerId={Container}&q=%D0%9D%D0%B5%D0%B4%D0%B5%D0%BB%D1%8F%20%D0%BC%D0%BE%D0%B4%D1%8B%20%26%20co"
            + "&type=event&domain=style-fashion&limit=5");
        await broker.ListPostsAsync(Container, entity, 2024, "report", 10, "ab+c/d=");
        handler.Paths[^1].Should().Be($"/api/entities/{entity}/posts?containerId={Container}&year=2024&role=report&limit=10&cursor=ab%2Bc%2Fd%3D");
        await broker.CalendarAsync(Container, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        handler.Paths[^1].Should().Be($"/api/entities/calendar?containerId={Container}&from=2026-01-01&to=2026-12-31");
        await broker.PullEventsAsync(Container, limit: 3, domain: "style-fashion");
        handler.Paths[^1].Should().Be($"/api/entities/events?containerId={Container}&limit=3&domain=style-fashion");
        await broker.GetAsync(Container, entity);
        handler.Paths[^1].Should().Be($"/api/entities/{entity}?containerId={Container}");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, AppErrorType.Validation)]
    [InlineData(HttpStatusCode.Unauthorized, AppErrorType.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, AppErrorType.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, AppErrorType.NotFound)]
    public async Task Get_ShouldMapTheStatus_WhenTheServiceAnswersAProblem(HttpStatusCode status, AppErrorType expected)
    {
        var (broker, _) = Broker(new StubHandler(status, """{"title":"Problem","detail":"No entity."}"""));

        var error = (await broker.GetAsync(Container, Guid.NewGuid())).Should().BeOfType<Result<EntityTimelineModel>.Failure>().Which.Error;

        (error.Type, error.Message).Should().Be((expected, "No entity."));
    }

    private static (IEntitiesBroker Broker, StubHandler Handler) Broker(StubHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHarvestClient(options =>
        {
            options.BaseAddress = new Uri("http://harvest.test");
            options.ApiKey = "hv_UnitTestKey000000000000000000000";
        });
        services.AddHarvestEntityClient();
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));
        return (services.BuildServiceProvider().GetRequiredService<IEntitiesBroker>(), handler);
    }

    /// <summary>Answers every request with one canned response and remembers each path.</summary>
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
