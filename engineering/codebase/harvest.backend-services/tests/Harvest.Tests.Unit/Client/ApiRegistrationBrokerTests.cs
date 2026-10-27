using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Client;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace Harvest.Tests.Unit.Client;

/// <summary>The client's mapping of the service's answers onto the contract's results; the happy path is conformance's.</summary>
public sealed class ApiRegistrationBrokerTests
{
    private const string Key = "hv_UnitTestKey000000000000000000000";

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, AppErrorType.Validation)]
    [InlineData(HttpStatusCode.Unauthorized, AppErrorType.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, AppErrorType.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, AppErrorType.NotFound)]
    [InlineData(HttpStatusCode.Conflict, AppErrorType.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable, AppErrorType.ExternalUnavailable)]
    public async Task RegisterTenant_ShouldMapTheStatus_WhenTheServiceAnswersAProblem(HttpStatusCode status, AppErrorType expected)
    {
        var (broker, _) = Broker(new StubHandler(status, """{"title":"Problem","detail":"The service said no."}"""));

        var error = Error(await broker.RegisterTenantAsync(new TenantRegistration { Slug = "events", Name = "Events" }));

        error.Type.Should().Be(expected);
        error.Message.Should().Be("The service said no.");
    }

    [Fact]
    public async Task GetTenant_ShouldFailSerialization_WhenTheBodyIsMalformed()
    {
        var (broker, _) = Broker(new StubHandler(HttpStatusCode.OK, "{not json"));

        Error(await broker.GetTenantAsync(Guid.NewGuid())).Type.Should().Be(AppErrorType.SerializationFailed);
    }

    [Fact]
    public async Task ListContainers_ShouldSendTheKeyAndTheTenantFilter_WhenATenantIsGiven()
    {
        var tenantId = Guid.NewGuid();
        var (broker, handler) = Broker(new StubHandler(HttpStatusCode.OK, """{"data":[]}"""));

        var result = await broker.ListContainersAsync(tenantId);

        result.IsSuccess.Should().BeTrue();
        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Be($"/api/containers?tenantId={tenantId}");
        handler.LastRequest.Headers.GetValues("X-Api-Key").Should().Equal(Key);
    }

    [Fact]
    public async Task RevokeApiKey_ShouldSendDelete_WhenAKeyIsRevoked()
    {
        var (tenantId, keyId) = (Guid.NewGuid(), Guid.NewGuid());
        var (broker, handler) = Broker(new StubHandler(HttpStatusCode.NotFound, string.Empty));

        Error(await broker.RevokeApiKeyAsync(tenantId, keyId)).Type.Should().Be(AppErrorType.NotFound);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be($"/api/tenants/{tenantId}/api-keys/{keyId}");
    }

    [Fact]
    public async Task GetPipeline_ShouldFailAsUnavailable_WhenTheServiceIsUnreachable()
    {
        var (broker, _) = Broker(new ThrowingHandler());

        Error(await broker.GetPipelineAsync(Guid.NewGuid())).Type.Should().Be(AppErrorType.ExternalUnavailable);
    }

    private static (IRegistrationBroker Broker, T Handler) Broker<T>(T handler) where T : HttpMessageHandler
    {
        var services = new ServiceCollection();
        services.AddHarvestClient(options =>
            {
                options.BaseAddress = new Uri("http://harvest.test");
                options.ApiKey = Key;
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return (services.BuildServiceProvider().GetRequiredService<IRegistrationBroker>(), handler);
    }

    private static AppError Error<T>(Result<T> result) where T : notnull =>
        result.Should().BeOfType<Result<T>.Failure>().Which.Error;

    /// <summary>Answers every request with one canned response and remembers the request.</summary>
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    /// <summary>Fails every request as a refused connection.</summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("refused");
    }
}
