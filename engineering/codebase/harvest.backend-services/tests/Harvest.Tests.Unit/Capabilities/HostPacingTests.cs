using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Pacing;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Unit.Capabilities;

/// <summary>The pacing and back-off math: the polite gap, the 2 s floor, the 30 s crawl ceiling, the rate-limit wait and
/// the widening pause a refusal earns.</summary>
public sealed class HostPacingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly FetchingOptions Options = new();

    [Fact]
    public void BaseDelay_ShouldHoldTheTwoSecondFloor_WhenNothingAsksForMore()
    {
        HostPacing.BaseDelay(crawlDelay: null, stepMinDelay: null, Options).Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void BaseDelay_ShouldTakeTheCrawlDelay_WhenItIsLongerThanTheFloor()
    {
        HostPacing.BaseDelay(TimeSpan.FromSeconds(10), null, Options).Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void BaseDelay_ShouldCapTheCrawlDelay_WhenItExceedsThirtySeconds()
    {
        HostPacing.BaseDelay(TimeSpan.FromSeconds(120), null, Options).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void BaseDelay_ShouldTakeTheStepMinimum_WhenItIsTheLongest()
    {
        HostPacing.BaseDelay(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8), Options).Should().Be(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void Plan_ShouldResetAndPaceByBase_WhenTheFetchSucceeds()
    {
        var plan = Plan(FetchOutcome.Ok, adaptiveDelay: TimeSpan.FromMinutes(1), consecutiveFailures: 3);

        plan.ConsecutiveFailures.Should().Be(0);
        plan.AdaptiveDelay.Should().Be(TimeSpan.FromSeconds(2));
        plan.NextAllowedAtUtc.Should().Be(Now + TimeSpan.FromSeconds(2));
        plan.PausedUntilUtc.Should().BeNull();
        plan.PauseReason.Should().BeNull();
    }

    [Fact]
    public void Plan_ShouldWaitAtLeastTheRetryAfter_WhenRateLimited()
    {
        var plan = Plan(FetchOutcome.RateLimited, retryAfter: TimeSpan.FromSeconds(120));

        plan.NextAllowedAtUtc.Should().Be(Now + TimeSpan.FromSeconds(120));
        plan.RetryAfterUntilUtc.Should().Be(Now + TimeSpan.FromSeconds(120));
        plan.PausedUntilUtc.Should().Be(Now + TimeSpan.FromSeconds(120));
        plan.ConsecutiveFailures.Should().Be(1);
        plan.PauseReason.Should().Be(PauseReasons.RateLimit);
    }

    [Fact]
    public void Plan_ShouldHonourARetryAfterBeyondTheMaxDelay_WhenTheHostAsksForLong()
    {
        var plan = Plan(FetchOutcome.RateLimited, retryAfter: TimeSpan.FromMinutes(10));

        // The adaptive delay is capped, but an explicit Retry-After is obeyed in full.
        plan.NextAllowedAtUtc.Should().Be(Now + TimeSpan.FromMinutes(10));
        plan.AdaptiveDelay.Should().BeLessThanOrEqualTo(Options.MaxDelay);
    }

    [Fact]
    public void Plan_ShouldPauseAndWiden_WhenTheHostRefuses()
    {
        var first = Plan(FetchOutcome.Blocked, adaptiveDelay: TimeSpan.FromSeconds(2), consecutiveFailures: 0);
        first.PausedUntilUtc.Should().Be(Now + TimeSpan.FromSeconds(30));
        first.AdaptiveDelay.Should().Be(TimeSpan.FromSeconds(4));
        first.PauseReason.Should().Be(PauseReasons.Blocked);

        var second = Plan(FetchOutcome.Blocked, adaptiveDelay: first.AdaptiveDelay, consecutiveFailures: 1);
        second.PausedUntilUtc.Should().Be(Now + TimeSpan.FromSeconds(60));
        second.ConsecutiveFailures.Should().Be(2);
    }

    [Fact]
    public void Plan_ShouldNameTheChallenge_WhenAChallengeStandsInTheWay()
    {
        Plan(FetchOutcome.Challenge).PauseReason.Should().Be(PauseReasons.Challenge);
    }

    [Fact]
    public void Plan_ShouldNeitherFailNorPause_WhenRobotsDisallowsThePath()
    {
        var plan = Plan(FetchOutcome.RobotsDisallowed, consecutiveFailures: 1);

        plan.ConsecutiveFailures.Should().Be(1);
        plan.PausedUntilUtc.Should().BeNull();
        plan.NextAllowedAtUtc.Should().Be(Now + TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Plan_ShouldWidenWithoutPausing_WhenTheConnectionErrors()
    {
        var plan = Plan(FetchOutcome.NetworkError, adaptiveDelay: TimeSpan.FromSeconds(4), consecutiveFailures: 1);

        plan.ConsecutiveFailures.Should().Be(2);
        plan.AdaptiveDelay.Should().Be(TimeSpan.FromSeconds(8));
        plan.PausedUntilUtc.Should().BeNull();
        plan.NextAllowedAtUtc.Should().Be(Now + TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void Plan_ShouldCapTheAdaptiveDelay_AtTheMaxDelay()
    {
        var plan = Plan(FetchOutcome.NetworkError, adaptiveDelay: TimeSpan.FromMinutes(4), consecutiveFailures: 5);

        plan.AdaptiveDelay.Should().Be(Options.MaxDelay);
    }

    [Fact]
    public void DueAt_ShouldTakeTheLatestGate()
    {
        var next = Now + TimeSpan.FromSeconds(2);
        var paused = Now + TimeSpan.FromSeconds(60);
        HostPacing.DueAt(next, paused, retryAfterUntilUtc: null).Should().Be(paused);
        HostPacing.DueAt(next, pausedUntilUtc: null, retryAfterUntilUtc: null).Should().Be(next);
    }

    private static PacingDecision Plan(
        FetchOutcome outcome,
        TimeSpan? adaptiveDelay = null,
        int consecutiveFailures = 0,
        TimeSpan? crawlDelay = null,
        TimeSpan? stepMinDelay = null,
        TimeSpan? retryAfter = null) =>
        HostPacing.Plan(outcome, adaptiveDelay ?? TimeSpan.FromSeconds(2), consecutiveFailures, crawlDelay, stepMinDelay, retryAfter, Now, Options);
}
