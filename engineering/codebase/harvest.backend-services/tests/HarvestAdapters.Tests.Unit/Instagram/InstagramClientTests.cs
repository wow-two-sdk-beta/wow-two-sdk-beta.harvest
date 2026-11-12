using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Unit.Integrations;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
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
        settings.PageSize = 0;
        adapter.DescribeConfiguration(source).Readiness.Should().Be(SourceReadiness.ConfigurationRequired);
        settings.PageSize = 25;
        settings.AllowedUsernames = [];
        adapter.DescribeConfiguration(source).Readiness.Should().Be(SourceReadiness.ConfigurationRequired);
        settings.AllowedUsernames = ["approved"];
        settings.ProfessionalAccountId = "not-a-numeric-id";
        adapter.DescribeConfiguration(source).Readiness.Should().Be(SourceReadiness.ConfigurationRequired);
    }

    [Fact]
    public async Task Disabled_missing_credentials_and_unapproved_accounts_never_send_requests()
    {
        var http = new StubHttpFactory();
        var settings = Settings();
        var client = new InstagramBusinessDiscoveryClient(http, Options.Create(settings), TimeProvider.System);
        settings.Enabled = false;
        (await client.ReadAsync("teplofest")).Outcome.Should().Be(FetchOutcome.CapabilityUnavailable);
        settings.Enabled = true;
        settings.AccessToken = "";
        (await client.ReadAsync("teplofest")).Outcome.Should().Be(FetchOutcome.CapabilityUnavailable);
        settings.AccessToken = "secret-test-token";
        (await client.ReadAsync("unapproved")).Outcome.Should().Be(FetchOutcome.NotApproved);
        (await client.ReadAsync("teplofest){id}")).Outcome.Should().Be(FetchOutcome.NotApproved);
        (await client.ReadAsync("teplofest", "cursor){id}")).Outcome.Should().Be(FetchOutcome.UnexpectedContent);
        http.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Business_discovery_uses_header_credentials_and_rebuilds_pagination_on_the_fixed_origin()
    {
        var http = new StubHttpFactory();
        http.Replies.Enqueue("""{"business_discovery":{"username":"teplofest","media":{"data":[],"paging":{"cursors":{"after":"opaqueCursor"},"next":"https://attacker.example/?access_token=must-not-follow"}}}}""");
        http.Replies.Enqueue("""{"business_discovery":{"username":"teplofest","media":{"data":[]}}}""");
        var client = new InstagramBusinessDiscoveryClient(http, Options.Create(Settings()), TimeProvider.System);
        var first = await client.ReadAsync("teplofest");
        first.Outcome.Should().Be(FetchOutcome.Ok);
        first.After.Should().Be("opaqueCursor");
        (await client.ReadAsync("teplofest", first.After)).Outcome.Should().Be(FetchOutcome.Ok);
        http.Calls.Should().HaveCount(2).And.OnlyContain(call => call.Uri.Host == "graph.facebook.com" && call.Uri.Scheme == "https"
            && call.Headers["Authorization"] == "Bearer secret-test-token" && !call.Uri.AbsoluteUri.Contains("secret-test-token"));
        Uri.UnescapeDataString(http.Calls[1].Uri.Query).Should().Contain("business_discovery.username(teplofest)").And.Contain(".after(opaqueCursor)");
        first.Usage.Requests.Should().Be(1);
        first.Usage.Bytes.Should().BePositive();
        first.RequestUrl.Should().Be(http.Calls[0].Uri.AbsoluteUri).And.NotContain("secret-test-token");
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
        var result = await new InstagramBusinessDiscoveryClient(http, Options.Create(Settings()), TimeProvider.System).ReadAsync("teplofest");
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
        http.Replies.Enqueue(JsonSerializer.Serialize(new { business_discovery = new { username = "teplofest", media = new { data = Array.Empty<object>(), paging = values } } }));
        var result = await new InstagramBusinessDiscoveryClient(http, Options.Create(Settings()), TimeProvider.System).ReadAsync("teplofest");
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
        (await new InstagramBusinessDiscoveryClient(http, Options.Create(Settings()), TimeProvider.System).ReadAsync("teplofest"))
            .Outcome.Should().Be(expected);
        http.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Oversized_responses_and_wrong_publishers_fail_explicitly()
    {
        var http = new StubHttpFactory();
        var settings = Settings();
        settings.MaximumResponseBytes = 1024;
        http.Replies.Enqueue(new string('x', 2048));
        var client = new InstagramBusinessDiscoveryClient(http, Options.Create(settings), TimeProvider.System);
        (await client.ReadAsync("teplofest")).Outcome.Should().Be(FetchOutcome.ResponseTooLarge);
        http.Replies.Enqueue("""{"business_discovery":{"username":"other","media":{"data":[]}}}""");
        (await client.ReadAsync("teplofest")).Outcome.Should().Be(FetchOutcome.Restricted);
    }

    [Fact]
    public async Task Cancellation_and_timeout_are_distinct()
    {
        var http = new StubHttpFactory { BeforeReply = token => Task.Delay(Timeout.Infinite, token) };
        var settings = Settings();
        settings.Timeout = TimeSpan.FromMilliseconds(10);
        var client = new InstagramBusinessDiscoveryClient(http, Options.Create(settings), TimeProvider.System);
        (await client.ReadAsync("teplofest")).Outcome.Should().Be(FetchOutcome.FetchTimeout);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadAsync("teplofest", cancellationToken: cancellation.Token));
    }

    internal static InstagramOptions Settings() => new()
    {
        Enabled = true, AccessToken = "secret-test-token", ProfessionalAccountId = "123456789", ApiVersion = "v1.0",
        AllowedUsernames = ["teplofest"], MaximumPages = 2,
    };
}
