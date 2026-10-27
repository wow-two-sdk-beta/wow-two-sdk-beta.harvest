using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Pacing;

/// <summary>The politeness math: from a fetch's outcome it works out when a host may be fetched next, how wide its
/// adaptive delay has grown, and whether a lane is paused.</summary>
/// <remarks>A challenge, a block or a rate limit widens the back-off and is never retried past; a rate limit waits at
/// least as long as the host asked. One connection per host is the engine's to hold; this only sets the gap.</remarks>
internal static class HostPacing
{
    /// <summary>The least gap between fetches of one host, whatever a source or step asks for.</summary>
    public static readonly TimeSpan MinDelayFloor = TimeSpan.FromSeconds(2);

    /// <summary>The most a robots <c>crawl_delay</c> is honoured to; a longer one is capped here.</summary>
    public static readonly TimeSpan CrawlDelayCeiling = TimeSpan.FromSeconds(30);

    /// <summary>The first pause a refusal earns; it doubles with each repeat, up to the configured maximum.</summary>
    public static readonly TimeSpan RefusalPauseBase = TimeSpan.FromSeconds(30);

    /// <summary>Caps a robots <c>crawl_delay</c> at the ceiling; a negative one is dropped.</summary>
    /// <param name="crawlDelay">The delay robots stated, or null.</param>
    public static TimeSpan? ClampCrawlDelay(TimeSpan? crawlDelay) =>
        crawlDelay is { } delay && delay > TimeSpan.Zero ? (delay < CrawlDelayCeiling ? delay : CrawlDelayCeiling) : null;

    /// <summary>The polite gap for a host: the longest of the 2 s floor, the operator's minimum, the step's minimum and
    /// the capped robots crawl delay.</summary>
    /// <param name="crawlDelay">The robots crawl delay, or null.</param>
    /// <param name="stepMinDelay">The step's own minimum, or null.</param>
    /// <param name="options">The fetching options holding the operator minimum.</param>
    public static TimeSpan BaseDelay(TimeSpan? crawlDelay, TimeSpan? stepMinDelay, FetchingOptions options)
    {
        var gap = MinDelayFloor;
        if (options.MinDelay > gap)
            gap = options.MinDelay;
        if (stepMinDelay is { } step && step > gap)
            gap = step;
        if (ClampCrawlDelay(crawlDelay) is { } crawl && crawl > gap)
            gap = crawl;
        return gap;
    }

    /// <summary>Works out the host's pacing after a fetch: the next-allowed time, the widened adaptive delay, the pause and
    /// the failure count.</summary>
    /// <param name="outcome">What the fetch came back as.</param>
    /// <param name="adaptiveDelay">The host's current adaptive delay.</param>
    /// <param name="consecutiveFailures">The host's current run of failures.</param>
    /// <param name="crawlDelay">The robots crawl delay, or null.</param>
    /// <param name="stepMinDelay">The step's own minimum, or null.</param>
    /// <param name="retryAfter">The wait the host asked for, or null.</param>
    /// <param name="now">The clock.</param>
    /// <param name="options">The fetching options.</param>
    public static PacingDecision Plan(
        FetchOutcome outcome,
        TimeSpan adaptiveDelay,
        int consecutiveFailures,
        TimeSpan? crawlDelay,
        TimeSpan? stepMinDelay,
        TimeSpan? retryAfter,
        DateTimeOffset now,
        FetchingOptions options)
    {
        var baseDelay = BaseDelay(crawlDelay, stepMinDelay, options);

        switch (Category(outcome))
        {
            case Kind.Success:
                // The host answered cleanly: forget the failures and relax the gap back to polite.
                return new PacingDecision(baseDelay, now + baseDelay, null, null, 0, null, baseDelay);

            case Kind.Skip:
                // A robots-disallowed path or a step the host never saw: pace, but neither fail nor pause the host.
                return new PacingDecision(Clamp(adaptiveDelay, baseDelay, options), now + baseDelay, null, null, consecutiveFailures, null, baseDelay);

            case Kind.RateLimited:
            {
                var failures = consecutiveFailures + 1;
                var widened = Widen(adaptiveDelay, baseDelay, options);
                var wait = retryAfter is { } asked && asked > baseDelay ? asked : baseDelay;
                if (widened > wait)
                    wait = widened;
                var until = now + wait;
                return new PacingDecision(widened, until, retryAfter is { } ra ? now + ra : null, until, failures, PauseReasons.RateLimit, baseDelay);
            }

            case Kind.Refusal:
            {
                var failures = consecutiveFailures + 1;
                var widened = Widen(adaptiveDelay, baseDelay, options);
                var pauseFor = RefusalPause(failures, options);
                var until = now + pauseFor;
                return new PacingDecision(widened, until, null, until, failures, ReasonFor(outcome), baseDelay);
            }

            default: // Kind.TransientError
            {
                var failures = consecutiveFailures + 1;
                var widened = Widen(adaptiveDelay, baseDelay, options);
                return new PacingDecision(widened, now + widened, null, null, failures, null, baseDelay);
            }
        }
    }

    /// <summary>The time a host becomes fetchable again: the latest of its next-allowed, its pause and its
    /// <c>Retry-After</c> gates.</summary>
    /// <param name="nextAllowedAtUtc">The pacing gate.</param>
    /// <param name="pausedUntilUtc">The pause gate, or null.</param>
    /// <param name="retryAfterUntilUtc">The rate-limit gate, or null.</param>
    public static DateTimeOffset DueAt(DateTimeOffset nextAllowedAtUtc, DateTimeOffset? pausedUntilUtc, DateTimeOffset? retryAfterUntilUtc)
    {
        var due = nextAllowedAtUtc;
        if (pausedUntilUtc is { } paused && paused > due)
            due = paused;
        if (retryAfterUntilUtc is { } retry && retry > due)
            due = retry;
        return due;
    }

    private static TimeSpan Widen(TimeSpan adaptiveDelay, TimeSpan baseDelay, FetchingOptions options)
    {
        var doubled = adaptiveDelay > TimeSpan.Zero ? adaptiveDelay + adaptiveDelay : baseDelay;
        return Clamp(doubled, baseDelay, options);
    }

    private static TimeSpan Clamp(TimeSpan delay, TimeSpan baseDelay, FetchingOptions options)
    {
        if (delay < baseDelay)
            delay = baseDelay;
        return delay > options.MaxDelay ? options.MaxDelay : delay;
    }

    private static TimeSpan RefusalPause(int failures, FetchingOptions options)
    {
        var pause = RefusalPauseBase;
        for (var repeat = 1; repeat < failures; repeat++)
        {
            pause += pause;
            if (pause >= options.MaxDelay)
                return options.MaxDelay;
        }

        return pause > options.MaxDelay ? options.MaxDelay : pause;
    }

    private static Kind Category(FetchOutcome outcome) => outcome switch
    {
        FetchOutcome.Ok or FetchOutcome.NotModified or FetchOutcome.Gone => Kind.Success,
        FetchOutcome.RobotsDisallowed or FetchOutcome.Restricted or FetchOutcome.NotApproved
            or FetchOutcome.RenderRequired or FetchOutcome.CapabilityUnavailable => Kind.Skip,
        FetchOutcome.RateLimited => Kind.RateLimited,
        FetchOutcome.Blocked or FetchOutcome.Challenge or FetchOutcome.WaitingRoom or FetchOutcome.LoginRequired => Kind.Refusal,
        _ => Kind.TransientError,
    };

    private static string ReasonFor(FetchOutcome outcome) => outcome switch
    {
        FetchOutcome.Blocked => PauseReasons.Blocked,
        FetchOutcome.Challenge => PauseReasons.Challenge,
        FetchOutcome.WaitingRoom => PauseReasons.WaitingRoom,
        FetchOutcome.LoginRequired => PauseReasons.LoginRequired,
        _ => PauseReasons.Blocked,
    };

    private enum Kind
    {
        Success,
        Skip,
        RateLimited,
        Refusal,
        TransientError,
    }
}

/// <summary>The pacing the host carries after a fetch.</summary>
/// <param name="AdaptiveDelay">The host's new adaptive gap.</param>
/// <param name="NextAllowedAtUtc">When the host may be fetched again.</param>
/// <param name="RetryAfterUntilUtc">The rate-limit gate, or null.</param>
/// <param name="PausedUntilUtc">The lane's pause gate, or null.</param>
/// <param name="ConsecutiveFailures">The host's new run of failures.</param>
/// <param name="PauseReason">Why the lane is paused, or null.</param>
/// <param name="BaseDelay">The polite gap the next-allowed time was built from.</param>
internal sealed record PacingDecision(
    TimeSpan AdaptiveDelay,
    DateTimeOffset NextAllowedAtUtc,
    DateTimeOffset? RetryAfterUntilUtc,
    DateTimeOffset? PausedUntilUtc,
    int ConsecutiveFailures,
    string? PauseReason,
    TimeSpan BaseDelay);

/// <summary>The reasons a lane pauses, stored against the host and lane.</summary>
internal static class PauseReasons
{
    public const string RateLimit = "rate_limit";

    public const string Blocked = "blocked";

    public const string Challenge = "challenge";

    public const string WaitingRoom = "waiting_room";

    public const string LoginRequired = "login_required";
}
