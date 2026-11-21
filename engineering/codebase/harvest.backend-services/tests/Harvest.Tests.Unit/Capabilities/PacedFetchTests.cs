using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Unit.Capabilities;

/// <summary>A request that carries a pipeline's own pace reserves its start under the host lock, sends outside it and records its
/// result under a fresh, short lock; every other request behaves as it did.</summary>
public sealed class PacedFetchTests
{
    private static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task Paced_ShouldReserveItsOwnGapBelowTheHostMinimumTheHostPaceAndTheReadersGap()
    {
        var host = Create();
        host.Transport.Responses.Enqueue(FetchTestHost.Response());

        var result = await host.Fetcher.FetchAsync(Request(Gap) with { MinDelay = TimeSpan.FromSeconds(3) });

        result.Outcome.Should().Be(FetchOutcome.Ok);
        result.Usage.Requests.Should().Be(1);
        (host.Hosts.State.NextAllowedAt - host.Clock.Now).Should().Be(Gap);
    }

    [Fact]
    public async Task Unpaced_ShouldKeepTheLongestOfTheHostMinimumThePaceAndTheReadersGap()
    {
        var host = Create();
        host.Transport.Responses.Enqueue(FetchTestHost.Response());

        await host.Fetcher.FetchAsync(Request(pace: null) with { MinDelay = TimeSpan.FromSeconds(3) });

        (host.Hosts.State.NextAllowedAt - host.Clock.Now).Should().Be(TimeSpan.FromSeconds(3));
        host.Hosts.Acquisitions.Should().Be(1);
    }

    [Fact]
    public async Task Paced_ShouldFallBackToTheHostGap_WhenThePipelineSetsNoGapOfItsOwn()
    {
        var host = Create();
        host.Transport.Responses.Enqueue(FetchTestHost.Response());

        await host.Fetcher.FetchAsync(Request(pace: new RequestPace()) with { MinDelay = TimeSpan.FromSeconds(3) });

        (host.Hosts.State.NextAllowedAt - host.Clock.Now).Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Paced_ShouldRaiseItsGapToARobotsCrawlDelay()
    {
        var host = Create();
        host.Hosts.State.RobotsBody = "User-agent: *\nCrawl-delay: 5\nAllow: /";
        host.Hosts.State.RobotsOrigin = "https://example.com";
        host.Hosts.State.RobotsStatus = 200;
        host.Hosts.State.RobotsCachedAt = host.Clock.Now;
        host.Transport.Responses.Enqueue(FetchTestHost.Response());

        var result = await host.Fetcher.FetchAsync(Request(Gap) with { RobotsPolicy = RobotsPolicy.Obey });

        result.Outcome.Should().Be(FetchOutcome.Ok);
        (host.Hosts.State.NextAllowedAt - host.Clock.Now).Should().Be(TimeSpan.FromSeconds(5));
        host.Hosts.State.CrawlDelaySeconds.Should().Be(5);
        host.Transport.Requests.Select(item => item.Uri.AbsolutePath).Should().Equal("/event");
    }

    [Theory]
    [InlineData(8000, 8000)]
    [InlineData(2000, 250)]
    [InlineData(1000, 250)]
    public async Task Paced_ShouldFollowADelayARefusalRaised_ButNotTheRestingOne(int adaptiveMs, int expectedMs)
    {
        var host = Create();
        host.Hosts.State.AdaptiveDelayMilliseconds = adaptiveMs;
        host.Transport.Responses.Enqueue(FetchTestHost.Response());

        await host.Fetcher.FetchAsync(Request(Gap));

        (host.Hosts.State.NextAllowedAt - host.Clock.Now).Should().Be(TimeSpan.FromMilliseconds(expectedMs));
    }

    [Fact]
    public async Task Paced_ShouldStartTwoRequestsOneGapApartAndOverlapThemInFlight()
    {
        var host = Create();
        var starts = new List<long>();
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = Stopwatch.StartNew();
        host.Transport.SendOverride = async (_, cancellation) =>
        {
            int index;
            lock (starts)
            {
                starts.Add(clock.ElapsedMilliseconds);
                index = starts.Count;
            }
            // The first reply waits for the second request to start: with the lock held through a request, this never ends.
            if (index == 1)
                await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellation);
            else
                secondStarted.TrySetResult();
            return FetchTestHost.Response();
        };

        var first = host.Fetcher.FetchAsync(Request(Gap));
        while (host.Hosts.Disposals < 1)
            await Task.Delay(5); // The first request reserved its start and released the lock; its reply is still in flight.
        var second = host.Fetcher.FetchAsync(Request(Gap));
        var answers = await Task.WhenAll(first, second);

        answers.Should().OnlyContain(answer => answer.Outcome == FetchOutcome.Ok);
        starts.Should().HaveCount(2);
        (starts[1] - starts[0]).Should().BeGreaterThanOrEqualTo(200, "the second start waits for the first request's reserved gap");
        host.Hosts.Disposals.Should().Be(host.Hosts.Acquisitions);
    }

    [Fact]
    public async Task Paced_ShouldNotClearAPauseThatARefusalSetAfterItsStart()
    {
        var host = Create();
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Hosts.State.ConsecutiveFailures = 2;
        host.Hosts.State.PauseReason = nameof(FetchOutcome.HttpError);
        host.Hosts.State.PausedUntil = host.Clock.Now.AddMinutes(-1);
        host.Transport.SendOverride = async (_, cancellation) =>
        {
            sent.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellation);
            return FetchTestHost.Response();
        };

        var request = host.Fetcher.FetchAsync(Request(Gap));
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // A sibling's refusal lands while this reply is in flight.
        host.Hosts.State.ConsecutiveFailures = 1;
        host.Hosts.State.PauseReason = nameof(FetchOutcome.RateLimited);
        host.Hosts.State.PausedUntil = host.Clock.Now.AddMinutes(15);
        host.Hosts.State.RetryAfterUntil = host.Clock.Now.AddMinutes(20);
        release.SetResult();
        var result = await request;

        result.Outcome.Should().Be(FetchOutcome.Ok);
        host.Hosts.State.PausedUntil.Should().Be(host.Clock.Now.AddMinutes(15));
        host.Hosts.State.PauseReason.Should().Be(nameof(FetchOutcome.RateLimited));
        host.Hosts.State.ConsecutiveFailures.Should().Be(1);
        host.Hosts.State.RetryAfterUntil.Should().Be(host.Clock.Now.AddMinutes(20));
    }

    [Fact]
    public async Task Paced_ShouldClearAPauseThatEndedBeforeItsStart_AndSetRetryAfter()
    {
        var host = Create();
        host.Hosts.State.ConsecutiveFailures = 2;
        host.Hosts.State.PauseReason = nameof(FetchOutcome.HttpError);
        host.Hosts.State.PausedUntil = host.Clock.Now.AddMinutes(-1);
        host.Hosts.State.AdaptiveDelayMilliseconds = 10_000;
        var reply = FetchTestHost.Response();
        reply.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        host.Transport.Responses.Enqueue(reply);

        var result = await host.Fetcher.FetchAsync(Request(Gap));

        result.Outcome.Should().Be(FetchOutcome.Ok);
        host.Hosts.State.PausedUntil.Should().BeNull();
        host.Hosts.State.PauseReason.Should().BeNull();
        host.Hosts.State.ConsecutiveFailures.Should().Be(0);
        host.Hosts.State.RetryAfterUntil.Should().Be(host.Clock.Now.AddSeconds(30));
        host.Hosts.State.AdaptiveDelayMilliseconds.Should().Be(9000, "a reply decays the raised delay by a tenth");
        (host.Hosts.State.NextAllowedAt - host.Clock.Now).Should().Be(TimeSpan.FromSeconds(10), "the raised delay spaced the start that was reserved before the reply");
    }

    [Theory]
    [InlineData("http500", FetchOutcome.HttpError)]
    [InlineData("network", FetchOutcome.NetworkError)]
    [InlineData("oversize", FetchOutcome.ResponseTooLarge)]
    public async Task Paced_ShouldPauseTheHost_WhenTheReplyFailsAsItDoesUnpaced(string scenario, FetchOutcome expected)
    {
        foreach (bool paced in new[] { true, false })
        {
            var host = Create();
            switch (scenario)
            {
                case "http500": host.Transport.Responses.Enqueue(FetchTestHost.Response(HttpStatusCode.InternalServerError)); break;
                case "network": host.Transport.SendOverride = (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("failed")); break;
                default:
                    host.Options.MaximumResponseBytes = 1;
                    host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "content"));
                    break;
            }

            var result = await host.Fetcher.FetchAsync(Request(paced ? Gap : null));

            result.Outcome.Should().Be(expected, paced ? "paced" : "unpaced");
            host.Hosts.State.PausedUntil.Should().NotBeNull(paced ? "paced" : "unpaced");
            host.Hosts.State.PauseReason.Should().Be(expected.ToString());
            host.Hosts.State.ConsecutiveFailures.Should().Be(1);
            host.Hosts.State.AdaptiveDelayMilliseconds.Should().BeGreaterThan(0);
            host.Hosts.Disposals.Should().Be(host.Hosts.Acquisitions);
        }
    }

    [Fact]
    public async Task Paced_ShouldRefuseWithoutSendingAnything_WhileTheHostIsPaused()
    {
        var host = Create();
        host.Hosts.State.PausedUntil = host.Clock.Now.AddMinutes(5);
        host.Hosts.State.PauseReason = nameof(FetchOutcome.RateLimited);

        var result = await host.Fetcher.FetchAsync(Request(Gap));

        result.Outcome.Should().Be(FetchOutcome.RateLimited);
        result.RetryAfter.Should().Be(TimeSpan.FromMinutes(5));
        host.Transport.Requests.Should().BeEmpty();
        host.Hosts.Disposals.Should().Be(1);
    }

    [Theory]
    [InlineData(nameof(FetchOutcome.RateLimited), true)]
    [InlineData(nameof(FetchOutcome.Blocked), true)]
    [InlineData(nameof(FetchOutcome.RateLimited), false)]
    public async Task Paced_ShouldNotSend_WhenASiblingsRefusalPausedTheHostWhileItWaitedForItsReservedStart(string reason, bool pausedUntil)
    {
        var host = Create();
        // A sibling's reservation puts this request's start 400 ms ahead.
        host.Hosts.State.NextAllowedAt = host.Clock.Now.AddMilliseconds(400);
        host.Transport.Responses.Enqueue(FetchTestHost.Response());

        var request = host.Fetcher.FetchAsync(Request(Gap));
        while (host.Hosts.Disposals < 1)
            await Task.Delay(5); // The request reserved its start and released the lock; it is waiting for that start.
        host.Hosts.State.PauseReason = reason;
        if (pausedUntil)
            host.Hosts.State.PausedUntil = host.Clock.Now.AddMinutes(5);
        else
            host.Hosts.State.RetryAfterUntil = host.Clock.Now.AddMinutes(5);
        var result = await request;

        result.Outcome.Should().Be(Enum.Parse<FetchOutcome>(reason));
        result.RetryAfter.Should().Be(TimeSpan.FromMinutes(5));
        result.Usage.Requests.Should().Be(0, "a request the pause stopped sends nothing");
        host.Transport.Requests.Should().BeEmpty("the pause is checked again before the request goes out");
        host.Hosts.Disposals.Should().Be(host.Hosts.Acquisitions);
    }

    [Fact]
    public async Task Paced_ShouldSendAfterItsWait_WhenNothingPausedTheHostMeanwhile()
    {
        var host = Create();
        host.Hosts.State.NextAllowedAt = host.Clock.Now.AddMilliseconds(100);
        host.Transport.Responses.Enqueue(FetchTestHost.Response());

        var result = await host.Fetcher.FetchAsync(Request(Gap));

        result.Outcome.Should().Be(FetchOutcome.Ok);
        host.Transport.Requests.Should().ContainSingle();
        host.Hosts.Acquisitions.Should().Be(3, "a reservation, the check after the wait and the record of the reply");
        host.Hosts.Disposals.Should().Be(3);
    }

    [Fact]
    public async Task Paced_ShouldTakeAShortLeasePerHopAndRecordItsResult_WhenRobotsAndARedirectAreInvolved()
    {
        var host = Create();
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "User-agent: *\nAllow: /", type: "text/plain"));
        var redirect = FetchTestHost.Response(HttpStatusCode.Redirect);
        redirect.Headers.Location = new Uri("/final", UriKind.Relative);
        host.Transport.Responses.Enqueue(redirect);
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "final"));

        var result = await host.Fetcher.FetchAsync(Request(Gap) with { RobotsPolicy = RobotsPolicy.Obey });

        result.Outcome.Should().Be(FetchOutcome.Ok);
        result.Document!.FinalUrl.AbsolutePath.Should().Be("/final");
        host.Transport.Requests.Select(item => item.Uri.AbsolutePath).Should().Equal("/robots.txt", "/event", "/final");
        host.Hosts.Acquisitions.Should().Be(5, "a reservation and a pause check after the wait for each of the two hops, and one lease to record the reply");
        host.Hosts.Disposals.Should().Be(5);
    }

    [Fact]
    public async Task Paced_ShouldReplayTheCachedBodyOfA304()
    {
        var host = Create();
        var ten = TimeSpan.FromMilliseconds(10);
        var first = FetchTestHost.Response(body: "cached-body");
        first.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
        host.Transport.Responses.Enqueue(first);
        host.Transport.Responses.Enqueue(FetchTestHost.Response(HttpStatusCode.NotModified, string.Empty));

        await host.Fetcher.FetchAsync(Request(ten));
        var second = await host.Fetcher.FetchAsync(Request(ten));

        second.NotModified.Should().BeTrue();
        System.Text.Encoding.UTF8.GetString(second.Document!.Body.Span).Should().Be("cached-body");
        host.Transport.Requests[1].Headers["If-None-Match"].Should().Be("\"v1\"");
    }

    [Fact]
    public async Task Paced_ShouldNeverMoveTheNextAllowedTimeBackward()
    {
        var host = Create();
        var far = host.Clock.Now.AddMinutes(10);
        host.Transport.Responses.Enqueue(FetchTestHost.Response());
        host.Transport.SendOverride = (_, _) =>
        {
            // Another pipeline reserved a start far ahead while this request was in flight.
            host.Hosts.State.NextAllowedAt = far;
            return Task.FromResult(FetchTestHost.Response());
        };

        await host.Fetcher.FetchAsync(Request(Gap));

        host.Hosts.State.NextAllowedAt.Should().Be(far);
    }

    private static FetchTestHost Create()
    {
        var host = new FetchTestHost();
        host.Options.MinimumDelay = TimeSpan.FromSeconds(2);
        host.Hosts.State.MinimumDelayMilliseconds = 2000;
        host.Hosts.State.AdaptiveDelayMilliseconds = 2000;
        return host;
    }

    private static FetchRequest Request(TimeSpan? gap = null, RequestPace? pace = null) => new()
    {
        Uri = new Uri("https://example.com/event"), RobotsPolicy = RobotsPolicy.Ignore,
        Pace = pace ?? (gap is { } own ? new RequestPace { Gap = own } : null),
    };
}
