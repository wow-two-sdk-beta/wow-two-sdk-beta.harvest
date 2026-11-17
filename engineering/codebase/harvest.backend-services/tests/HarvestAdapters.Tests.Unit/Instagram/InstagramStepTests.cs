using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Rendering;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

namespace HarvestAdapters.Tests.Unit.Instagram;

public sealed class InstagramStepTests
{
    [Fact]
    public async Task Pagination_is_bounded_and_never_claims_complete_enumeration()
    {
        var client = new FixtureClient();
        var broker = new InstagramStepBroker(client);
        var context = new Context();
        var first = await broker.ExecuteAsync(Item("https://www.instagram.com/teplofest/"), context);
        first.Captures.Should().ContainSingle();
        first.EnumerationComplete.Should().BeFalse();
        first.Next.Should().ContainSingle();
        first.Next.Single().Target.Should().StartWith("https://www.instagram.com/teplofest/?__harvest_cursor=");
        first.Next.Single().Target.Should().NotContain("opaque-cursor");
        var continuation = first.Next.Single();
        var last = await broker.ExecuteAsync(Item(continuation.Target) with { Payload = continuation.Payload }, context);
        last.Next.Should().BeEmpty();
        last.Message.Should().Contain("page limit");
        last.EnumerationComplete.Should().BeFalse();
        client.Calls.Should().Equal(("teplofest", 7, (string?)null), ("teplofest", 7, "opaque-cursor"));
        context.Signals.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(FetchOutcome.RateLimited)]
    [InlineData(FetchOutcome.Blocked)]
    public async Task A_closed_request_gate_prevents_provider_access_and_preserves_the_retry_delay(FetchOutcome outcome)
    {
        var client = new FixtureClient();
        var context = new Context { RequestOutcome = outcome, RequestRetryAfter = TimeSpan.FromSeconds(17) };

        var result = await new InstagramStepBroker(client).ExecuteAsync(Item("https://www.instagram.com/teplofest/"), context);

        result.Outcome.Should().Be(outcome);
        result.RetryAfter.Should().Be(context.RequestRetryAfter);
        result.Usage.Requests.Should().Be(0);
        client.Calls.Should().BeEmpty();
        var lease = context.RequestLeases.Should().ContainSingle().Subject;
        lease.Completed.Should().BeEmpty();
        lease.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(FetchOutcome.Ok)]
    [InlineData(FetchOutcome.RateLimited)]
    [InlineData(FetchOutcome.LoginRequired)]
    public async Task Provider_outcomes_complete_and_release_the_shared_request_lease(FetchOutcome outcome)
    {
        var client = new FixtureClient { Outcome = outcome };
        var context = new Context();

        var result = await new InstagramStepBroker(client).ExecuteAsync(Item("https://www.instagram.com/teplofest/"), context);

        result.Outcome.Should().Be(outcome);
        client.Calls.Should().ContainSingle();
        var lease = context.RequestLeases.Should().ContainSingle().Subject;
        lease.Completed.Should().Equal(outcome);
        lease.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://www.instagram.com/p/ABC/")]
    [InlineData("https://example.test/teplofest/")]
    [InlineData("https://user:password@www.instagram.com/teplofest/")]
    [InlineData("https://www.instagram.com/accounts/")]
    public async Task Non_profile_targets_do_not_call_the_provider(string target)
    {
        var client = new FixtureClient();
        await new InstagramStepBroker(client).ExecuteAsync(Item(target), new Context());
        client.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://www.instagram.com/another.publisher/")]
    [InlineData("https://instagram.com/teplofest/")]
    public async Task Target_outside_the_frozen_profile_scope_is_refused_without_provider_access(string target)
    {
        var client = new FixtureClient();

        var result = await new InstagramStepBroker(client).ExecuteAsync(Item(target), new Context());

        result.Outcome.Should().Be(FetchOutcome.NotApproved);
        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_saved_profile_scope_is_not_permission_to_collect_a_target()
    {
        var client = new FixtureClient();

        var result = await new InstagramStepBroker(client).ExecuteAsync(Item("https://www.instagram.com/teplofest/"),
            new Context { Scope = new() });

        result.Outcome.Should().Be(FetchOutcome.NotApproved);
        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_pipeline_cannot_collect_a_different_account_than_its_publisher_source()
    {
        var client = new FixtureClient();

        var result = await new InstagramStepBroker(client).ExecuteAsync(Item("https://www.instagram.com/teplofest/"),
            new Context { SourceHandle = "teplostore_2022" });

        result.Outcome.Should().Be(FetchOutcome.NotApproved);
        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Omitted_page_size_uses_the_provider_default_without_a_host_setting()
    {
        var client = new FixtureClient();

        await new InstagramStepBroker(client).ExecuteAsync(Item("https://www.instagram.com/teplofest/"),
            new Context { Limits = new() { MaxPages = 1 } });

        client.Calls.Should().ContainSingle().Which.Should().Be(("teplofest", 25, (string?)null));
    }

    [Theory]
    [InlineData("{\"after\":\"cursor\",\"page\":0}")]
    [InlineData("{\"after\":\"cursor\",\"page\":2}")]
    [InlineData("{\"page\":1}")]
    [InlineData("{\"after\":42,\"page\":1}")]
    [InlineData("[]")]
    [InlineData("\"cursor\"")]
    public async Task Invalid_continuation_or_exhausted_saved_limit_is_rejected_before_fetching(string payload)
    {
        var client = new FixtureClient();
        using var document = JsonDocument.Parse(payload);
        var item = Item("https://www.instagram.com/teplofest/?__harvest_cursor=opaque") with
        {
            Payload = document.RootElement.Clone(),
        };

        var result = await new InstagramStepBroker(client).ExecuteAsync(item, new Context());

        result.Outcome.Should().Be(FetchOutcome.UnexpectedContent);
        client.Calls.Should().BeEmpty();
    }

    private static StepItem Item(string target) => new()
    {
        WorkItemId = Guid.NewGuid(), RunId = Guid.NewGuid(), PipelineId = Guid.NewGuid(), ContainerId = Guid.NewGuid(),
        SourceId = Guid.NewGuid(), StepKey = "enumerate", HostKey = "www.instagram.com", Lane = "direct", Target = target,
    };
    private sealed class FixtureClient : IInstagramMediaBroker
    {
        internal FetchOutcome Outcome { get; init; } = FetchOutcome.Ok;
        internal List<(string, int, string?)> Calls { get; } = [];
        public Task<InstagramMediaPage> ReadAsync(string username, int pageSize = 25, string? after = null, CancellationToken cancellationToken = default)
        {
            Calls.Add((username, pageSize, after));
            return Task.FromResult(new InstagramMediaPage
            {
                Outcome = Outcome, After = "opaque-cursor",
                Media = [InstagramCaptureTests.Media("Join us at the fashion market", 1)], Usage = new() { Requests = 1, Bytes = 100 },
            });
        }
    }
    private sealed class Context : IStepContext
    {
        internal List<StepSignal> Signals { get; } = [];
        internal List<RequestLease> RequestLeases { get; } = [];
        public FetchOutcome RequestOutcome { get; init; } = FetchOutcome.Ok;
        public TimeSpan? RequestRetryAfter { get; init; }
        public PipelineScope Scope { get; init; } = new() { Url = "https://www.instagram.com/teplofest/" };
        public PipelineLimits Limits { get; init; } = new() { PageSize = 7, MaxPages = 2 };
        public ResolvedPlan Plan => throw new NotSupportedException();
        public string SourceHandle { get; init; } = "teplofest";
        public SourceModel Source => new()
        {
            Id = Guid.NewGuid(), ContainerId = Guid.NewGuid(), Family = SourceFamily.Instagram, Handle = SourceHandle,
            Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window, Posture = SourcePosture.Official,
            Permission = SourcePermission.Open, AiAllowed = false, PoolShareable = false, RobotsPolicy = RobotsPolicy.Obey,
            ConsentBasis = ConsentBasis.None, Authority = 50, RetentionClass = "standard", CapabilityPolicy = new(),
            State = SourceState.Active, StateSinceUtc = DateTimeOffset.UnixEpoch, CreatedAtUtc = DateTimeOffset.UnixEpoch,
        };
        public HttpClient Http => throw new NotSupportedException();
        public IBrowserRenderer? Browser => null;
        public IChallengeDetector Challenges => throw new NotSupportedException();
        public TimeProvider Clock => TimeProvider.System;
        public Task<ISourceRequestLease> AcquireRequestAsync(CancellationToken cancellationToken = default)
        {
            var lease = new RequestLease { Outcome = RequestOutcome, RetryAfter = RequestRetryAfter };
            RequestLeases.Add(lease);
            return Task.FromResult<ISourceRequestLease>(lease);
        }
        public void Observe(StepSignal signal) => Signals.Add(signal);
    }

    private sealed class RequestLease : ISourceRequestLease
    {
        public FetchOutcome Outcome { get; init; }
        public TimeSpan? RetryAfter { get; init; }
        internal List<FetchOutcome> Completed { get; } = [];
        internal bool Disposed { get; private set; }
        public Task CompleteAsync(FetchOutcome outcome, CancellationToken cancellationToken = default)
        {
            Completed.Add(outcome);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
