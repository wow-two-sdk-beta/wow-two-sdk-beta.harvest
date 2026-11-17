using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Unit.Integrations;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Integrations;
using WoW.Two.Sdk.Beta.Be.Integrations.Instagram;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

namespace HarvestAdapters.Tests.Unit.Instagram;

public sealed class InstagramClientTests
{
    [Fact]
    public void Console_readiness_uses_the_clients_local_validation_without_disclosing_settings()
    {
        var source = new SourceModel
        {
            Id = Guid.NewGuid(), ContainerId = Guid.NewGuid(), Family = SourceFamily.Instagram, Handle = "instagram.com",
            Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window, Posture = SourcePosture.Official,
            Permission = SourcePermission.Open, AiAllowed = false, PoolShareable = false, RobotsPolicy = RobotsPolicy.Obey,
            ConsentBasis = ConsentBasis.None, Authority = 50, RetentionClass = "standard", CapabilityPolicy = new(),
            State = SourceState.Active, StateSinceUtc = DateTimeOffset.UnixEpoch, CreatedAtUtc = DateTimeOffset.UnixEpoch,
        };
        InstagramOptions settings = Settings();
        var adapter = new InstagramSourceAdapter(Options.Create(settings));
        adapter.DescribeConfiguration(source).Readiness.Should().Be(SourceReadiness.Ready);
        JsonSerializer.Serialize(adapter.DescribeConfiguration(source)).Should().NotContain(settings.AccessToken).And.NotContain(settings.ProfessionalAccountId);
        settings.BaseAddress = "https://other.example/";
        adapter.DescribeConfiguration(source).Readiness.Should().Be(SourceReadiness.ConfigurationRequired);
        settings.BaseAddress = "https://graph.facebook.com/";
        settings.ProfessionalAccountId = "not-a-numeric-id";
        adapter.DescribeConfiguration(source).Readiness.Should().Be(SourceReadiness.ConfigurationRequired);
    }

    [Fact]
    public async Task Missing_connection_and_invalid_provider_arguments_never_send_requests()
    {
        var http = new StubHttpFactory();
        var settings = Settings();
        var client = Client(http, settings);
        settings.AccessToken = "";
        (await client.ReadAsync("teplofest")).Outcome.Should().Be(FetchOutcome.CapabilityUnavailable);
        settings.AccessToken = "secret-test-token";
        (await client.ReadAsync("teplofest){id}")).Outcome.Should().Be(FetchOutcome.NotApproved);
        (await client.ReadAsync("teplofest", pageSize: 0)).Outcome.Should().Be(FetchOutcome.NotApproved);
        (await client.ReadAsync("teplofest", pageSize: 51)).Outcome.Should().Be(FetchOutcome.NotApproved);
        (await client.ReadAsync("teplofest", after: "cursor){id}")).Outcome.Should().Be(FetchOutcome.UnexpectedContent);
        http.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Broker_uses_the_registered_publisher_argument_and_page_size_without_a_host_allowlist()
    {
        var http = new StubHttpFactory();
        http.Replies.Enqueue("""{"business_discovery":{"username":"another.publisher","media":{"data":[]}}}""");

        var result = await Client(http, Settings()).ReadAsync("another.publisher", pageSize: 7);

        result.Outcome.Should().Be(FetchOutcome.Ok);
        Uri.UnescapeDataString(http.Calls.Single().Uri.Query)
            .Should().Contain("business_discovery.username(another.publisher)").And.Contain("media.limit(7)");
    }

    [Fact]
    public async Task Business_discovery_uses_header_credentials_and_rebuilds_pagination_on_the_fixed_origin()
    {
        var http = new StubHttpFactory();
        http.Replies.Enqueue("""{"business_discovery":{"username":"teplofest","media":{"data":[{"id":"123"}],"paging":{"cursors":{"after":"opaqueCursor"},"next":"https://attacker.example/?access_token=must-not-follow"}}}}""");
        http.Replies.Enqueue("""{"business_discovery":{"username":"teplofest","media":{"data":[]}}}""");
        var client = Client(http, Settings());
        var first = await client.ReadAsync("teplofest");
        first.Outcome.Should().Be(FetchOutcome.Ok);
        first.After.Should().Be("opaqueCursor");
        (await client.ReadAsync("teplofest", after: first.After)).Outcome.Should().Be(FetchOutcome.Ok);
        http.Calls.Should().HaveCount(2).And.OnlyContain(call => call.Uri.Host == "graph.facebook.com" && call.Uri.Scheme == "https"
            && call.Headers["Authorization"] == "Bearer secret-test-token" && !call.Uri.AbsoluteUri.Contains("secret-test-token"));
        Uri.UnescapeDataString(http.Calls[1].Uri.Query).Should().Contain("business_discovery.username(teplofest)").And.Contain(".after(opaqueCursor)");
        first.Usage.Requests.Should().Be(1);
        first.Usage.Bytes.Should().BePositive();
        first.RequestUrl.Should().Be("https://graph.facebook.com/v1.0/123456789").And.NotContain("secret-test-token");
        first.HttpStatus.Should().Be(200);
    }

    [Theory]
    [InlineData(190, FetchOutcome.LoginRequired)]
    [InlineData(4, FetchOutcome.RateLimited)]
    [InlineData(613, FetchOutcome.RateLimited)]
    [InlineData(10, FetchOutcome.Restricted)]
    [InlineData(100, FetchOutcome.Restricted)]
    [InlineData(200, FetchOutcome.Restricted)]
    [InlineData(2, FetchOutcome.ProviderFailed)]
    public async Task Platform_errors_are_not_deletions_or_silent_empty_feeds(int code, FetchOutcome expected)
    {
        var http = new StubHttpFactory { Status = HttpStatusCode.BadRequest };
        http.Replies.Enqueue(JsonSerializer.Serialize(new { error = new { code, message = "sensitive-provider-detail" } }));
        var result = await Client(http, Settings()).ReadAsync("teplofest");
        result.Outcome.Should().Be(expected);
        result.Message.Should().NotContain("sensitive-provider-detail");
        if (expected == FetchOutcome.RateLimited) result.RetryAfter.Should().Be(TimeSpan.FromMinutes(15));
        http.Calls.Should().ContainSingle();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"cursors\":null}")]
    [InlineData("{\"cursors\":{\"after\":\"\"}}")]
    public async Task Pagination_next_without_a_valid_cursor_is_not_a_complete_page(string paging)
    {
        var http = new StubHttpFactory();
        using JsonDocument fields = JsonDocument.Parse(paging);
        var values = fields.RootElement.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone());
        values["next"] = JsonSerializer.SerializeToElement("https://graph.facebook.com/next");
        http.Replies.Enqueue(JsonSerializer.Serialize(new { business_discovery = new { username = "teplofest", media = new { data = new[] { new { id = "123" } }, paging = values } } }));
        var result = await Client(http, Settings()).ReadAsync("teplofest");
        result.Outcome.Should().Be(FetchOutcome.UnexpectedContent);
        result.After.Should().BeNull();
        http.Calls.Should().ContainSingle();
    }

    [Theory]
    [InlineData(429, FetchOutcome.RateLimited)]
    [InlineData(401, FetchOutcome.LoginRequired)]
    [InlineData(403, FetchOutcome.Restricted)]
    [InlineData(302, FetchOutcome.InvalidRedirect)]
    public async Task Status_failures_need_no_json_and_are_never_retried(int status, FetchOutcome expected)
    {
        var http = new StubHttpFactory { Status = (HttpStatusCode)status };
        http.Replies.Enqueue("<html>Refused</html>");
        (await Client(http, Settings()).ReadAsync("teplofest"))
            .Outcome.Should().Be(expected);
        http.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Oversized_responses_and_wrong_publishers_fail_explicitly()
    {
        var http = new StubHttpFactory();
        var settings = Settings();
        http.Replies.Enqueue(new string('x', 2048));
        var client = Client(http, settings, new() { BaseAddress = new Uri(settings.BaseAddress), MaximumResponseBytes = 1024 });
        (await client.ReadAsync("teplofest")).Outcome.Should().Be(FetchOutcome.ResponseTooLarge);
        http.Replies.Enqueue("""{"business_discovery":{"username":"other","media":{"data":[]}}}""");
        var wrongPublisher = await client.ReadAsync("teplofest");
        wrongPublisher.Outcome.Should().Be(FetchOutcome.UnexpectedContent);
        wrongPublisher.Media.Should().BeEmpty();
        wrongPublisher.After.Should().BeNull();
    }

    [Fact]
    public async Task Cancellation_and_timeout_are_distinct()
    {
        var http = new StubHttpFactory { BeforeReply = token => Task.Delay(Timeout.Infinite, token) };
        var settings = Settings();
        var client = Client(http, settings, new() { BaseAddress = new Uri(settings.BaseAddress), Timeout = TimeSpan.FromMilliseconds(10) });
        (await client.ReadAsync("teplofest")).Outcome.Should().Be(FetchOutcome.FetchTimeout);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadAsync("teplofest", cancellationToken: cancellation.Token));
    }

    private static InstagramMediaBroker Client(StubHttpFactory http, InstagramOptions settings, InstagramIntegrationOptions? transport = null) => new(
        new InstagramClient(http.CreateClient("fixture"), new TokenService(settings),
            transport ?? new() { BaseAddress = new Uri(settings.BaseAddress) }, TimeProvider.System), Options.Create(settings));

    private sealed class TokenService(InstagramOptions settings) : IAccessTokenService
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken ct = default) => Task.FromResult<string?>(settings.AccessToken);
    }

    internal static InstagramOptions Settings() => new()
    {
        AccessToken = "secret-test-token", ProfessionalAccountId = "123456789", ApiVersion = "v1.0",
    };
}
