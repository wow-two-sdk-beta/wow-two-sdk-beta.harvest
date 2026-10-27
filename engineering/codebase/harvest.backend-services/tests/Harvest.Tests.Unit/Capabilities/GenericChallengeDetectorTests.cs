using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Unit.Capabilities;

public sealed class GenericChallengeDetectorTests
{
    [Theory]
    [InlineData(403, "<html>Access denied</html>", FetchOutcome.Blocked, null)]
    [InlineData(200, "<html>Just a moment... /cdn-cgi/challenge-platform/</html>", FetchOutcome.Challenge, ChallengeKind.JsInterstitial)]
    [InlineData(200, "<html>Verify you are human <div class='g-recaptcha'></div></html>", FetchOutcome.Challenge, ChallengeKind.CaptchaRecaptcha)]
    [InlineData(200, "<html>You are now in line</html>", FetchOutcome.WaitingRoom, ChallengeKind.WaitingRoom)]
    [InlineData(200, "<html>Sign in to continue</html>", FetchOutcome.LoginRequired, ChallengeKind.LoginWall)]
    [InlineData(429, "<html>captcha</html>", FetchOutcome.RateLimited, null)]
    public void Detect_ShouldClassifyRefusals(int status, string body, FetchOutcome outcome, ChallengeKind? kind)
    {
        var clock = new FetchTestHost.TestClock();
        var verdict = new GenericChallengeDetector(clock).Detect(Probe(status, body));
        verdict!.Outcome.Should().Be(outcome);
        verdict.Kind.Should().Be(kind);
    }

    [Fact]
    public void Detect_ShouldLeaveAnEmbeddedContactFormReadable_WhenThePageHasAWidget()
    {
        new GenericChallengeDetector(TimeProvider.System).Detect(Probe(200,
            "<html><h1>Concert on Friday</h1><form><div class='g-recaptcha'></div></form></html>")).Should().BeNull();
    }

    [Fact]
    public void Detect_ShouldReadHttpDateRetryAfter_WhenUnavailable()
    {
        var clock = new FetchTestHost.TestClock();
        var verdict = new GenericChallengeDetector(clock).Detect(Probe(503, "busy") with
        {
            Headers = [new("Retry-After", clock.Now.AddMinutes(10).ToString("r"))]
        });
        verdict!.Outcome.Should().Be(FetchOutcome.RateLimited);
        verdict.RetryAfter.Should().Be(TimeSpan.FromMinutes(10));
    }

    private static ChallengeProbe Probe(int status, string body) => new()
    {
        RequestUri = new Uri("https://example.com/event"), FinalUri = new Uri("https://example.com/event"),
        StatusCode = status, ContentType = "text/html", Body = body
    };
}
