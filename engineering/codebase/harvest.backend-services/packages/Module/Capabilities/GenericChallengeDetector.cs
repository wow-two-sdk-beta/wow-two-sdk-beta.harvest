using System.Globalization;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Recognizes generic refusals and interstitials without attempting to clear them.</summary>
public sealed class GenericChallengeDetector(TimeProvider clock) : IChallengeDetector
{
    /// <inheritdoc />
    public string Key => "generic";

    /// <inheritdoc />
    public ChallengeVerdict? Detect(ChallengeProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var retryAfter = ParseRetryAfter(probe.Headers, clock.GetUtcNow());
        if (probe.StatusCode == 429 || probe.StatusCode == 503 && retryAfter is not null)
            return Verdict(FetchOutcome.RateLimited, "http_throttled", retryAfter: retryAfter);

        string text = probe.Body;
        bool html = probe.ContentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true
            || text.TrimStart().StartsWith('<');
        if (html)
        {
            if (Has(text, "cf-chl-", "/cdn-cgi/challenge-platform/", "Just a moment...", "Checking your browser"))
                return Verdict(FetchOutcome.Challenge, "javascript_interstitial", ChallengeKind.JsInterstitial, retryAfter);
            if (Has(text, "cf_waitingroom", "You are now in line", "You are in the waiting room"))
                return Verdict(FetchOutcome.WaitingRoom, "waiting_room", ChallengeKind.WaitingRoom, retryAfter);

            // Embedded widgets on an otherwise complete page are not proof of an interstitial.
            bool challengePage = Has(text, "verify you are human", "verify that you are human", "complete the captcha",
                "complete the security check", "captcha verification", "unusual traffic")
                || probe.StatusCode is 401 or 403 or 503;
            if (challengePage)
            {
                if (Has(text, "g-recaptcha", "recaptcha/api"))
                    return Verdict(FetchOutcome.Challenge, "recaptcha", ChallengeKind.CaptchaRecaptcha, retryAfter);
                if (Has(text, "h-captcha", "hcaptcha.com"))
                    return Verdict(FetchOutcome.Challenge, "hcaptcha", ChallengeKind.CaptchaHcaptcha, retryAfter);
                if (Has(text, "cf-turnstile", "challenges.cloudflare.com/turnstile"))
                    return Verdict(FetchOutcome.Challenge, "turnstile", ChallengeKind.CaptchaTurnstile, retryAfter);
                if (Has(text, "geetest", "gt_captcha"))
                    return Verdict(FetchOutcome.Challenge, "geetest", ChallengeKind.CaptchaGeetest, retryAfter);
                if (Has(text, "captcha", "verify you are human", "verify that you are human"))
                    return Verdict(FetchOutcome.Challenge, "human_verification", ChallengeKind.CaptchaOther, retryAfter);
            }

            if (Has(text, "sign in to continue", "log in to continue", "login required"))
                return Verdict(FetchOutcome.LoginRequired, "login_wall", ChallengeKind.LoginWall, retryAfter);
        }

        return probe.StatusCode is 401 or 403
            ? Verdict(FetchOutcome.Blocked, "http_refusal", retryAfter: retryAfter)
            : null;
    }

    internal static TimeSpan? ParseRetryAfter(IEnumerable<KeyValuePair<string, string>> headers, DateTimeOffset now)
    {
        string? value = headers.FirstOrDefault(header => header.Key.Equals("Retry-After", StringComparison.OrdinalIgnoreCase)).Value;
        if (value is null)
            return null;
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds) && seconds >= 0)
            return TimeSpan.FromSeconds(Math.Min(seconds, 31536000));
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            return date > now ? date - now : TimeSpan.Zero;
        return null;
    }

    private ChallengeVerdict Verdict(FetchOutcome outcome, string evidence, ChallengeKind? kind = null, TimeSpan? retryAfter = null) =>
        new() { Outcome = outcome, Kind = kind, RetryAfter = retryAfter, DetectorKey = Key, Evidence = evidence };

    private static bool Has(string text, params string[] markers) =>
        markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
