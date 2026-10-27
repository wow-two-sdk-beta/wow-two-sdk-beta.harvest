using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Challenges;

/// <summary>The generic detector that tells a challenge from a block from a rate limit, from a response's status, headers
/// and the start of its body.</summary>
/// <remarks>It only names what stood in the way; nothing here solves, clears or routes around it. A host-specific adapter
/// adds its own detector ahead of this one, and the first verdict wins.</remarks>
internal sealed class GenericChallengeDetector : IChallengeDetector
{
    /// <inheritdoc />
    public string Key => "generic";

    /// <inheritdoc />
    public ChallengeVerdict? Detect(ChallengeProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        var body = probe.Body.Length > 0 ? probe.Body.ToLowerInvariant() : string.Empty;

        // A waiting room or queue page comes first: it is not a captcha and not a flat block, and often answers 200.
        if (FindMarker(body, WaitingRoomMarkers) is { } queue)
            return Verdict(FetchOutcome.WaitingRoom, kind: ChallengeKind.WaitingRoom, probe: probe, evidence: queue);

        // A captcha or JavaScript interstitial, whatever status it rides on (403, 503 and 200 are all seen).
        foreach (var (marker, kind) in ChallengeMarkers)
            if (body.Contains(marker, StringComparison.Ordinal))
                return Verdict(FetchOutcome.Challenge, kind: kind, probe: probe, evidence: marker);

        if (HasHeader(probe, "cf-mitigated", "challenge"))
            return Verdict(FetchOutcome.Challenge, kind: ChallengeKind.JsInterstitial, probe: probe, evidence: "cf-mitigated: challenge");

        // A wall that asks to sign in — Harvest never signs in, so the content stays out of reach.
        if (FindMarker(body, LoginMarkers) is { } login)
            return Verdict(FetchOutcome.LoginRequired, kind: ChallengeKind.LoginWall, probe: probe, evidence: login);

        // A throttle the host asked us to wait out.
        if (probe.StatusCode == 429)
            return Verdict(FetchOutcome.RateLimited, kind: null, probe: probe, evidence: "429 Too Many Requests");
        if (probe.StatusCode == 503 && RetryAfterPolicy.TryParseSeconds(Header(probe, "retry-after")) is not null)
            return Verdict(FetchOutcome.RateLimited, kind: null, probe: probe, evidence: "503 with Retry-After");

        // A flat refusal, with no challenge to pass.
        if (probe.StatusCode == 401)
            return Verdict(FetchOutcome.LoginRequired, kind: ChallengeKind.LoginWall, probe: probe, evidence: "401 Unauthorized");
        if (probe.StatusCode is 403 or 451)
            return Verdict(FetchOutcome.Blocked, kind: null, probe: probe, evidence: $"{probe.StatusCode} refusal");

        // Anything else is content, or a plain error the caller maps from the status.
        return null;
    }

    /// <summary>Builds a verdict, carrying any seconds-form <c>Retry-After</c> the host sent.</summary>
    private ChallengeVerdict Verdict(FetchOutcome outcome, ChallengeKind? kind, ChallengeProbe probe, string evidence) => new()
    {
        Outcome = outcome,
        Kind = kind,
        RetryAfter = RetryAfterPolicy.TryParseSeconds(Header(probe, "retry-after")),
        DetectorKey = Key,
        Evidence = evidence,
    };

    /// <summary>Returns the first body marker that matches, or null.</summary>
    private static string? FindMarker(string body, string[] markers)
    {
        foreach (var marker in markers)
            if (body.Contains(marker, StringComparison.Ordinal))
                return marker;
        return null;
    }

    /// <summary>Reads the first value of a header by name, case-insensitively.</summary>
    private static string? Header(ChallengeProbe probe, string name)
    {
        foreach (var header in probe.Headers)
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
                return header.Value;
        return null;
    }

    /// <summary>Tests whether a header's value contains <paramref name="valueContains"/>, case-insensitively.</summary>
    private static bool HasHeader(ChallengeProbe probe, string name, string valueContains)
    {
        foreach (var header in probe.Headers)
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)
                && header.Value.Contains(valueContains, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>The captcha and interstitial markers, most specific first, each with the kind it names.</summary>
    private static readonly (string Marker, ChallengeKind Kind)[] ChallengeMarkers =
    [
        ("challenges.cloudflare.com/turnstile", ChallengeKind.CaptchaTurnstile),
        ("cf-turnstile", ChallengeKind.CaptchaTurnstile),
        ("www.google.com/recaptcha", ChallengeKind.CaptchaRecaptcha),
        ("grecaptcha", ChallengeKind.CaptchaRecaptcha),
        ("hcaptcha.com", ChallengeKind.CaptchaHcaptcha),
        ("h-captcha", ChallengeKind.CaptchaHcaptcha),
        ("geetest", ChallengeKind.CaptchaGeetest),
        ("px-captcha", ChallengeKind.CaptchaFriction),
        ("perimeterx", ChallengeKind.CaptchaFriction),
        ("datadome", ChallengeKind.CaptchaOther),
        ("/cdn-cgi/challenge-platform", ChallengeKind.JsInterstitial),
        ("just a moment...", ChallengeKind.JsInterstitial),
        ("checking your browser", ChallengeKind.JsInterstitial),
        ("_incapsula_resource", ChallengeKind.JsInterstitial),
    ];

    /// <summary>The waiting-room markers.</summary>
    private static readonly string[] WaitingRoomMarkers =
    [
        "queue-it.net",
        "cf-waiting-room",
        "you are now in line",
        "waiting room",
    ];

    /// <summary>The sign-in-wall markers, kept narrow so an ordinary page with a login link is not mistaken for a wall.</summary>
    private static readonly string[] LoginMarkers =
    [
        "please log in to continue",
        "sign in to continue",
        "you must be logged in to view",
        "login required to access",
    ];
}
