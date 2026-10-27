using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Unit.Capabilities;

/// <summary>The generic classifier: it tells a challenge from a block from a rate limit, and leaves content alone. It only
/// names what stood in the way — nothing here solves or routes around it.</summary>
public sealed class GenericChallengeDetectorTests
{
    private readonly GenericChallengeDetector detector = new();

    [Theory]
    [InlineData("<script src=\"https://challenges.cloudflare.com/turnstile/v0/api.js\"></script>", ChallengeKind.CaptchaTurnstile)]
    [InlineData("<div class=\"cf-turnstile\" data-sitekey=\"x\"></div>", ChallengeKind.CaptchaTurnstile)]
    [InlineData("<script src=\"https://www.google.com/recaptcha/api.js\"></script>", ChallengeKind.CaptchaRecaptcha)]
    [InlineData("<div class=\"h-captcha\"></div>", ChallengeKind.CaptchaHcaptcha)]
    [InlineData("window.initGeetest({ gt: '...' })", ChallengeKind.CaptchaGeetest)]
    [InlineData("<title>Just a moment...</title>", ChallengeKind.JsInterstitial)]
    [InlineData("<form action=\"/_Incapsula_Resource\">", ChallengeKind.JsInterstitial)]
    public void Detect_ShouldNameAChallenge_WhenTheBodyCarriesItsMarker(string body, ChallengeKind expected)
    {
        var verdict = detector.Detect(Probe(statusCode: 403, body: body));

        verdict.Should().NotBeNull();
        verdict!.Outcome.Should().Be(FetchOutcome.Challenge);
        verdict.Kind.Should().Be(expected);
        verdict.DetectorKey.Should().Be("generic");
        verdict.Evidence.Should().NotBeEmpty();
    }

    [Fact]
    public void Detect_ShouldNameAChallenge_WhenCloudflareMitigatesWithAHeader()
    {
        var verdict = detector.Detect(Probe(statusCode: 403, headers: [new("cf-mitigated", "challenge")]));

        verdict!.Outcome.Should().Be(FetchOutcome.Challenge);
        verdict.Kind.Should().Be(ChallengeKind.JsInterstitial);
    }

    [Fact]
    public void Detect_ShouldNameAWaitingRoom_WhenTheQueuePageAnswers()
    {
        var verdict = detector.Detect(Probe(statusCode: 200, body: "<html>You are now in line. Powered by queue-it.net</html>"));

        verdict!.Outcome.Should().Be(FetchOutcome.WaitingRoom);
        verdict.Kind.Should().Be(ChallengeKind.WaitingRoom);
    }

    [Fact]
    public void Detect_ShouldNameARateLimit_WhenTheStatusIs429()
    {
        var verdict = detector.Detect(Probe(statusCode: 429, headers: [new("Retry-After", "120")]));

        verdict!.Outcome.Should().Be(FetchOutcome.RateLimited);
        verdict.RetryAfter.Should().Be(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void Detect_ShouldNameARateLimit_When503AsksToWait()
    {
        var verdict = detector.Detect(Probe(statusCode: 503, headers: [new("Retry-After", "30")]));

        verdict!.Outcome.Should().Be(FetchOutcome.RateLimited);
        verdict.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Detect_ShouldNotClaimARateLimit_When503CarriesNoRetryAfter()
    {
        detector.Detect(Probe(statusCode: 503)).Should().BeNull();
    }

    [Theory]
    [InlineData(403, FetchOutcome.Blocked)]
    [InlineData(451, FetchOutcome.Blocked)]
    [InlineData(401, FetchOutcome.LoginRequired)]
    public void Detect_ShouldNameARefusal_WhenTheStatusRefusesWithoutAChallenge(int status, FetchOutcome expected)
    {
        detector.Detect(Probe(statusCode: status))!.Outcome.Should().Be(expected);
    }

    [Fact]
    public void Detect_ShouldNameASignInWall_WhenTheBodySaysSo()
    {
        var verdict = detector.Detect(Probe(statusCode: 200, body: "<p>Please log in to continue.</p>"));

        verdict!.Outcome.Should().Be(FetchOutcome.LoginRequired);
        verdict.Kind.Should().Be(ChallengeKind.LoginWall);
    }

    [Theory]
    [InlineData(200, "<html><body><h1>Events this week</h1></body></html>")]
    [InlineData(200, "")]
    [InlineData(404, "<html>not found</html>")]
    public void Detect_ShouldReturnNull_WhenTheResponseIsContentOrAPlainError(int status, string body)
    {
        detector.Detect(Probe(statusCode: status, body: body)).Should().BeNull();
    }

    private static ChallengeProbe Probe(int statusCode, string body = "", IReadOnlyList<KeyValuePair<string, string>>? headers = null) => new()
    {
        RequestUri = new Uri("https://afisha.uz/ru/events"),
        FinalUri = new Uri("https://afisha.uz/ru/events"),
        StatusCode = statusCode,
        ContentType = "text/html",
        Headers = headers ?? [],
        Body = body,
    };
}
