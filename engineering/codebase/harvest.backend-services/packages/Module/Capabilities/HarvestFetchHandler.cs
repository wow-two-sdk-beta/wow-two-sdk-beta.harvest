using System.Net;
using System.Net.Http.Headers;
using System.Text;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Applies the same durable politeness policy to typed fetches and a step's raw HTTP client.</summary>
/// <remarks>A request holds its host's lock from the permission check through the body it reads, redirects and robots included,
/// so no two requests to a host overlap. A request that carries a <see cref="RequestPace"/> (a pipeline's own gap or parallel
/// fetches) takes the paced path: it holds the lock only to check, read robots when they are due and reserve its start time
/// (<c>NextAllowedAt</c> + its gap), sends and reads outside the lock, and takes the lock again, briefly, to record the result.
/// A <c>Crawl-delay</c>, a <c>Retry-After</c>, a pause and a delay a refusal raised bind both paths. Every request asks for <c>gzip</c>
/// and <c>br</c> and reads the raw stream: the usage counts the bytes on the wire, the size limit applies to the decoded bytes, and
/// everything after the read sees the decoded body.</remarks>
internal sealed class HarvestFetchHandler(
    IFetchTransportFactory transports,
    IHostFetchStore hosts,
    HarvestFetchOptions options,
    IChallengeDetector challenges,
    TimeProvider clock,
    CookieContainer? cookies = null,
    RouteRobotsCache? robotsByRoute = null) : HttpMessageHandler
{
    private RouteRobotsCache RouteRobots { get; } = robotsByRoute ?? new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var usage = new FetchUsageCounters();
        request.Options.Set(FetchUsageCounters.Key, usage);
        Uri? uri = request.RequestUri;
        if (!IsWebUri(uri) || request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            return Synthetic(request, FetchOutcome.InvalidRedirect, HttpStatusCode.BadRequest, "Only absolute HTTP(S) page reads are supported.");

        var policy = request.Options.TryGetValue(HarvestFetchMetadata.RequestKey, out var supplied)
            ? supplied
            : new FetchRequest { Uri = uri! };
        if (policy.DestinationPolicy is not null && !policy.DestinationPolicy.Allows(uri!))
            return Synthetic(request, FetchOutcome.NotApproved, HttpStatusCode.BadRequest, "The source does not permit this page route.");
        if (policy.Pace is { } pace)
            return await SendPacedAsync(request, policy, pace, uri!, usage, cancellationToken);
        await using var lease = await hosts.AcquireAsync(uri!.IdnHost.TrimEnd('.').ToLowerInvariant(), cancellationToken);
        try
        {
            if (lease.State.Permission is not ("open" or "granted") || lease.State.PolicyPermission is not ("open" or "granted"))
                return Synthetic(request, FetchOutcome.Restricted, HttpStatusCode.Forbidden, "Operator host policy does not permit this fetch.");
            var now = clock.GetUtcNow();
            var pausedUntil = Later(lease.State.PausedUntil, lease.State.RetryAfterUntil);
            if (pausedUntil > now)
            {
                var outcome = Enum.TryParse<FetchOutcome>(lease.State.PauseReason, out var previous) ? previous : FetchOutcome.RateLimited;
                return Synthetic(request, outcome, outcome == FetchOutcome.RateLimited ? HttpStatusCode.TooManyRequests : HttpStatusCode.Forbidden,
                    "Host remains paused after its previous response.", pausedUntil - now);
            }

            await using var route = await transports.AcquireAsync(uri, cookies, cancellationToken);
            if (!route.Allows(uri))
                return Synthetic(request, FetchOutcome.InvalidRedirect, HttpStatusCode.BadRequest, "Configured route does not allow the destination.");

            bool sharedCache = !route.IsPrivate && route.Cookies is null
                && !request.Headers.Contains("Authorization") && !request.Headers.Contains("Cookie");
            var robotsState = RobotsState(request, route, sharedCache, lease.State, uri.IdnHost.TrimEnd('.').ToLowerInvariant());
            TimeSpan gap = options.MinimumDelay;
            if (TimeSpan.FromMilliseconds(lease.State.MinimumDelayMilliseconds) > gap)
                gap = TimeSpan.FromMilliseconds(lease.State.MinimumDelayMilliseconds);
            if (lease.State.CrawlDelaySeconds is { } cachedCrawlDelay && TimeSpan.FromSeconds(cachedCrawlDelay) > gap)
                gap = TimeSpan.FromSeconds(cachedCrawlDelay);
            if (policy.MinDelay is { } requestedGap && requestedGap > gap)
                gap = requestedGap;
            if (TimeSpan.FromMilliseconds(lease.State.AdaptiveDelayMilliseconds) > gap)
                gap = TimeSpan.FromMilliseconds(lease.State.AdaptiveDelayMilliseconds);

            for (int redirects = 0; ; redirects++)
            {
                if (policy.RobotsPolicy == RobotsPolicy.Obey)
                {
                    var robots = await EnsureRobotsAsync(uri, route, lease, robotsState, gap, request, usage, cancellationToken);
                    if (robots.Failure is { } failure)
                        return failure;
                    var rules = RobotsRules.Parse(robotsState.RobotsBody ?? string.Empty, options.UserAgent);
                    robotsState.CrawlDelaySeconds = rules.CrawlDelay is { } parsedDelay ? (int)Math.Ceiling(parsedDelay.TotalSeconds) : null;
                    if (rules.CrawlDelay is { } crawlDelay && crawlDelay > gap)
                        gap = crawlDelay;
                    if (!rules.Allows(uri))
                        return Synthetic(request, FetchOutcome.RobotsDisallowed, HttpStatusCode.Forbidden, "Host robots policy disallows this path.");
                }

                var validators = sharedCache && policy.CacheResponse ? await lease.GetValidatorsAsync(uri.AbsoluteUri, cancellationToken) : null;
                HttpResponseMessage response = await ReadNetworkAsync(uri, route, request.Method, request, validators, lease, gap, usage, cancellationToken);
                int status = (int)response.StatusCode;
                if (IsRedirect(status))
                {
                    Uri? location = response.Headers.Location;
                    Uri? next = location is null ? null : location.IsAbsoluteUri ? location : new Uri(uri, location);
                    if (redirects >= options.MaximumRedirects || !IsSafeRedirect(uri, next) || !route.Allows(next!)
                        || policy.DestinationPolicy is not null && !policy.DestinationPolicy.Allows(next!))
                    {
                        response.Dispose();
                        return Synthetic(request, FetchOutcome.InvalidRedirect, HttpStatusCode.BadGateway, "Redirect left the source host or violated its transport policy.");
                    }
                    response.Dispose();
                    uri = next!;
                    continue;
                }

                byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (status == 304 && validators?.Body is { } cachedBody)
                {
                    if (cachedBody.Length > options.MaximumResponseBytes)
                    {
                        response.Dispose();
                        throw new ResponseTooLargeException();
                    }
                    response.Content.Dispose();
                    response.Content = new ByteArrayContent(cachedBody);
                    if (MediaTypeHeaderValue.TryParse(validators.ContentType, out var contentType))
                        response.Content.Headers.ContentType = contentType;
                    body = cachedBody;
                }
                var verdict = Detect(uri, response, body);
                if (verdict is not null)
                {
                    var backoff = Pause(lease.State, verdict.Outcome, verdict.RetryAfter);
                    request.Options.Set(HarvestFetchMetadata.ResponseKey, new(verdict.Outcome, verdict.Kind, backoff, verdict.Evidence));
                    return response;
                }

                var outcome = status is 404 or 410 ? FetchOutcome.Gone
                    : status == 304 || response.IsSuccessStatusCode ? FetchOutcome.Ok : FetchOutcome.HttpError;
                var retryAfter = GenericChallengeDetector.ParseRetryAfter(Headers(response), clock.GetUtcNow());
                if (outcome == FetchOutcome.HttpError)
                    retryAfter = Pause(lease.State, outcome, retryAfter);
                else
                {
                    lease.State.ConsecutiveFailures = 0;
                    lease.State.PauseReason = null;
                    lease.State.PausedUntil = null;
                    lease.State.RetryAfterUntil = retryAfter is { } wait ? clock.GetUtcNow() + wait : null;
                    lease.State.AdaptiveDelayMilliseconds = Math.Max((int)options.MinimumDelay.TotalMilliseconds,
                        (int)(lease.State.AdaptiveDelayMilliseconds * 0.9));
                    if (response.IsSuccessStatusCode && request.Method == HttpMethod.Get && sharedCache && policy.CacheResponse)
                    {
                        bool cacheable = response.Headers.CacheControl?.NoStore != true && response.Headers.CacheControl?.Private != true
                            && !response.Headers.Vary.Any(value => !value.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase))
                            && (response.Headers.ETag is not null || response.Content.Headers.LastModified is not null);
                        await lease.SaveValidatorsAsync(uri.AbsoluteUri, cacheable
                            ? new(response.Headers.ETag?.ToString(), response.Content.Headers.LastModified, body, response.Content.Headers.ContentType?.ToString())
                            : new(null, null), cancellationToken);
                    }
                }
                request.Options.Set(HarvestFetchMetadata.ResponseKey, new(outcome, RetryAfter: retryAfter));
                return response;
            }
        }
        catch (FetchTransportUnavailableException)
        {
            return Synthetic(request, FetchOutcome.CapabilityUnavailable, HttpStatusCode.ServiceUnavailable,
                "The configured route or session is unavailable. Check its provider settings.");
        }
        catch (HttpRequestException error) when (BlockedDestination(error))
        {
            return Synthetic(request, FetchOutcome.InvalidRedirect, HttpStatusCode.BadRequest, "Destination failed the public-network policy.");
        }
        catch (ResponseTooLargeException)
        {
            Pause(lease.State, FetchOutcome.ResponseTooLarge, null);
            return Synthetic(request, FetchOutcome.ResponseTooLarge, HttpStatusCode.RequestEntityTooLarge, "Response exceeded the configured byte limit.");
        }
        catch (ResponseDecodeException)
        {
            return Synthetic(request, FetchOutcome.DecodeError, HttpStatusCode.BadGateway, DecodeMessage);
        }
        catch (HttpRequestException)
        {
            var delay = Pause(lease.State, FetchOutcome.NetworkError, null);
            return Synthetic(request, FetchOutcome.NetworkError, HttpStatusCode.BadGateway, "Direct connection failed.", delay);
        }
        finally
        {
            // Persist a completed request's gate even if cancellation arrived during body reading.
            await lease.SaveAsync(CancellationToken.None);
        }
    }

    /// <summary>Sends a request the pipeline paces itself: under the host lock it checks, reads robots when they are due and
    /// reserves its start time; outside the lock it waits for that time, sends and reads; under a fresh, short lock it records
    /// the result. Each redirect hop repeats the three.</summary>
    private async Task<HttpResponseMessage> SendPacedAsync(HttpRequestMessage request, FetchRequest policy, RequestPace pace, Uri uri,
        FetchUsageCounters usage, CancellationToken cancellationToken)
    {
        string host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        var held = new HeldRoute();
        try
        {
            for (int redirects = 0; ; redirects++)
            {
                var reserved = await ReserveAsync(request, policy, pace, uri, host, held, usage, cancellationToken);
                if (reserved.Failure is { } failure)
                    return failure;
                var route = held.Route!;
                TimeSpan wait = reserved.Start - clock.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, clock, cancellationToken);
                    // A sibling reserved ahead of this request may have been refused while it waited, and the refusal paused the host.
                    if (await PausedWhileWaitingAsync(request, host, cancellationToken) is { } paused)
                        return paused;
                }
                HttpResponseMessage response = await ReadUnlockedAsync(uri, route, request.Method, request, reserved.Validators, usage, cancellationToken);
                int status = (int)response.StatusCode;
                if (IsRedirect(status))
                {
                    Uri? location = response.Headers.Location;
                    Uri? next = location is null ? null : location.IsAbsoluteUri ? location : new Uri(uri, location);
                    if (redirects >= options.MaximumRedirects || !IsSafeRedirect(uri, next) || !route.Allows(next!)
                        || policy.DestinationPolicy is not null && !policy.DestinationPolicy.Allows(next!))
                    {
                        response.Dispose();
                        return Synthetic(request, FetchOutcome.InvalidRedirect, HttpStatusCode.BadGateway, "Redirect left the source host or violated its transport policy.");
                    }
                    response.Dispose();
                    uri = next!;
                    continue;
                }

                byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (status == 304 && reserved.Validators?.Body is { } cachedBody)
                {
                    if (cachedBody.Length > options.MaximumResponseBytes)
                    {
                        response.Dispose();
                        throw new ResponseTooLargeException();
                    }
                    response.Content.Dispose();
                    response.Content = new ByteArrayContent(cachedBody);
                    if (MediaTypeHeaderValue.TryParse(reserved.Validators.ContentType, out var contentType))
                        response.Content.Headers.ContentType = contentType;
                    body = cachedBody;
                }
                var verdict = Detect(uri, response, body);
                if (verdict is not null)
                {
                    var backoff = await RecordPauseAsync(host, verdict.Outcome, verdict.RetryAfter);
                    request.Options.Set(HarvestFetchMetadata.ResponseKey, new(verdict.Outcome, verdict.Kind, backoff, verdict.Evidence));
                    return response;
                }

                var outcome = status is 404 or 410 ? FetchOutcome.Gone
                    : status == 304 || response.IsSuccessStatusCode ? FetchOutcome.Ok : FetchOutcome.HttpError;
                var retryAfter = GenericChallengeDetector.ParseRetryAfter(Headers(response), clock.GetUtcNow());
                if (outcome == FetchOutcome.HttpError)
                    retryAfter = await RecordPauseAsync(host, outcome, retryAfter);
                else
                    await RecordSuccessAsync(host, uri, request.Method, policy, response, body, retryAfter, reserved);
                request.Options.Set(HarvestFetchMetadata.ResponseKey, new(outcome, RetryAfter: retryAfter));
                return response;
            }
        }
        catch (FetchTransportUnavailableException)
        {
            return Synthetic(request, FetchOutcome.CapabilityUnavailable, HttpStatusCode.ServiceUnavailable,
                "The configured route or session is unavailable. Check its provider settings.");
        }
        catch (HttpRequestException error) when (BlockedDestination(error))
        {
            return Synthetic(request, FetchOutcome.InvalidRedirect, HttpStatusCode.BadRequest, "Destination failed the public-network policy.");
        }
        catch (ResponseTooLargeException)
        {
            await RecordPauseAsync(host, FetchOutcome.ResponseTooLarge, null);
            return Synthetic(request, FetchOutcome.ResponseTooLarge, HttpStatusCode.RequestEntityTooLarge, "Response exceeded the configured byte limit.");
        }
        catch (ResponseDecodeException)
        {
            return Synthetic(request, FetchOutcome.DecodeError, HttpStatusCode.BadGateway, DecodeMessage);
        }
        catch (HttpRequestException)
        {
            var delay = await RecordPauseAsync(host, FetchOutcome.NetworkError, null);
            return Synthetic(request, FetchOutcome.NetworkError, HttpStatusCode.BadGateway, "Direct connection failed.", delay);
        }
        finally
        {
            if (held.Route is not null)
                await held.Route.DisposeAsync();
        }
    }

    /// <summary>Takes the host lock, makes the checks the unpaced path makes, reads robots when they are due, and reserves the
    /// request's start: the later of now and the host's next allowed time, which moves on by the request's gap. The lock is
    /// released before it returns.</summary>
    private async Task<Reservation> ReserveAsync(HttpRequestMessage request, FetchRequest policy, RequestPace pace, Uri uri, string host,
        HeldRoute held, FetchUsageCounters usage, CancellationToken cancellationToken)
    {
        await using var lease = await hosts.AcquireAsync(host, cancellationToken);
        try
        {
            if (lease.State.Permission is not ("open" or "granted") || lease.State.PolicyPermission is not ("open" or "granted"))
                return new(Synthetic(request, FetchOutcome.Restricted, HttpStatusCode.Forbidden, "Operator host policy does not permit this fetch."));
            var now = clock.GetUtcNow();
            if (PausedResponse(request, lease.State, now) is { } paused)
                return new(paused);

            held.Route ??= await transports.AcquireAsync(uri, cookies, cancellationToken);
            var route = held.Route;
            if (!route.Allows(uri))
                return new(Synthetic(request, FetchOutcome.InvalidRedirect, HttpStatusCode.BadRequest, "Configured route does not allow the destination."));

            bool sharedCache = !route.IsPrivate && route.Cookies is null
                && !request.Headers.Contains("Authorization") && !request.Headers.Contains("Cookie");
            var robotsState = RobotsState(request, route, sharedCache, lease.State, host);
            TimeSpan gap;
            TimeSpan adaptive = TimeSpan.FromMilliseconds(lease.State.AdaptiveDelayMilliseconds);
            if (pace.Gap is { } own)
            {
                // The pipeline's own gap replaces the host's pace, the reader's gap and the fetch minimum; what a host or its robots
                // file demands, and a delay a refusal raised above the minimum, still bind.
                gap = own;
                if (lease.State.CrawlDelaySeconds is { } cachedCrawlDelay && TimeSpan.FromSeconds(cachedCrawlDelay) > gap)
                    gap = TimeSpan.FromSeconds(cachedCrawlDelay);
                if (adaptive > options.MinimumDelay && adaptive > gap)
                    gap = adaptive;
            }
            else
            {
                gap = options.MinimumDelay;
                if (TimeSpan.FromMilliseconds(lease.State.MinimumDelayMilliseconds) > gap)
                    gap = TimeSpan.FromMilliseconds(lease.State.MinimumDelayMilliseconds);
                if (lease.State.CrawlDelaySeconds is { } cachedCrawlDelay && TimeSpan.FromSeconds(cachedCrawlDelay) > gap)
                    gap = TimeSpan.FromSeconds(cachedCrawlDelay);
                if (policy.MinDelay is { } requestedGap && requestedGap > gap)
                    gap = requestedGap;
                if (adaptive > gap)
                    gap = adaptive;
            }

            if (policy.RobotsPolicy == RobotsPolicy.Obey)
            {
                var robots = await EnsureRobotsAsync(uri, route, lease, robotsState, gap, request, usage, cancellationToken);
                if (robots.Failure is { } failure)
                    return new(failure);
                var rules = RobotsRules.Parse(robotsState.RobotsBody ?? string.Empty, options.UserAgent);
                robotsState.CrawlDelaySeconds = rules.CrawlDelay is { } parsedDelay ? (int)Math.Ceiling(parsedDelay.TotalSeconds) : null;
                if (rules.CrawlDelay is { } crawlDelay && crawlDelay > gap)
                    gap = crawlDelay;
                if (!rules.Allows(uri))
                    return new(Synthetic(request, FetchOutcome.RobotsDisallowed, HttpStatusCode.Forbidden, "Host robots policy disallows this path."));
            }

            var validators = sharedCache && policy.CacheResponse ? await lease.GetValidatorsAsync(uri.AbsoluteUri, cancellationToken) : null;
            var start = clock.GetUtcNow();
            if (lease.State.NextAllowedAt > start)
                start = lease.State.NextAllowedAt;
            lease.State.NextAllowedAt = start + gap;
            return new(null, start, validators, sharedCache);
        }
        finally
        {
            // The reservation is durable before the lock goes, even when cancellation arrived while a robots file was read.
            await lease.SaveAsync(CancellationToken.None);
        }
    }

    /// <summary>Takes the host lock briefly, after a request waited for its reserved start, to see whether the host was paused in
    /// the meantime: a refusal of a sibling that started earlier pauses the host for every request, including one already
    /// reserved. The fetch lane sees the host's pause, which every refusal sets; the engine's own lane pause follows from the
    /// refused item's result.</summary>
    /// <returns>The refusal to return in place of sending, or null when the host is open.</returns>
    private async Task<HttpResponseMessage?> PausedWhileWaitingAsync(HttpRequestMessage request, string host, CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(options.Timeout);
        await using var lease = await hosts.AcquireAsync(host, bounded.Token);
        return PausedResponse(request, lease.State, clock.GetUtcNow());
    }

    /// <summary>Answers a request the host's pause stops: the outcome that set the pause, for as long as the pause or a
    /// <c>Retry-After</c> lasts; null when the host is open.</summary>
    private static HttpResponseMessage? PausedResponse(HttpRequestMessage request, HostFetchState state, DateTimeOffset now)
    {
        var pausedUntil = Later(state.PausedUntil, state.RetryAfterUntil);
        if (pausedUntil is not { } until || until <= now)
            return null;
        var outcome = Enum.TryParse<FetchOutcome>(state.PauseReason, out var previous) ? previous : FetchOutcome.RateLimited;
        return Synthetic(request, outcome, outcome == FetchOutcome.RateLimited ? HttpStatusCode.TooManyRequests : HttpStatusCode.Forbidden,
            "Host remains paused after its previous response.", until - now);
    }

    private async Task<HttpResponseMessage> ReadUnlockedAsync(Uri uri, IFetchTransportLease route, HttpMethod method, HttpRequestMessage original,
        ResponseValidators? validators, FetchUsageCounters usage, CancellationToken cancellationToken)
    {
        using var message = CreateMessage(uri, route, method, original, validators);
        return await SendAndReadAsync(message, uri, route, method, usage, cancellationToken);
    }

    /// <summary>Takes the host lock briefly to pause the host after a refusal, an HTTP error, a network error or an oversize reply.</summary>
    /// <returns>How long the host is paused.</returns>
    private async Task<TimeSpan> RecordPauseAsync(string host, FetchOutcome outcome, TimeSpan? retryAfter)
    {
        using var bounded = new CancellationTokenSource(options.Timeout);
        await using var lease = await hosts.AcquireAsync(host, bounded.Token);
        try
        {
            return Pause(lease.State, outcome, retryAfter);
        }
        finally
        {
            await lease.SaveAsync(CancellationToken.None);
        }
    }

    /// <summary>Takes the host lock briefly to record a reply that was read: it clears the host's failure state unless a pause is
    /// running that began after this request's start (a sibling's refusal, which a reply in flight never lifts), decays the
    /// delay a refusal raised, sets <c>Retry-After</c> and saves the validators. It never moves the next allowed time.</summary>
    private async Task RecordSuccessAsync(string host, Uri uri, HttpMethod method, FetchRequest policy, HttpResponseMessage response,
        byte[] body, TimeSpan? retryAfter, Reservation reserved)
    {
        using var bounded = new CancellationTokenSource(options.Timeout);
        await using var lease = await hosts.AcquireAsync(host, bounded.Token);
        try
        {
            var state = lease.State;
            var now = clock.GetUtcNow();
            if (state.PausedUntil is not { } paused || paused <= reserved.Start)
            {
                state.ConsecutiveFailures = 0;
                state.PauseReason = null;
                state.PausedUntil = null;
            }
            DateTimeOffset? stated = retryAfter is { } wait ? now + wait : null;
            state.RetryAfterUntil = state.RetryAfterUntil is { } siblings && siblings > reserved.Start ? Later(siblings, stated) : stated;
            state.AdaptiveDelayMilliseconds = Math.Max((int)options.MinimumDelay.TotalMilliseconds,
                (int)(state.AdaptiveDelayMilliseconds * 0.9));
            if (response.IsSuccessStatusCode && method == HttpMethod.Get && reserved.SharedCache && policy.CacheResponse)
            {
                bool cacheable = response.Headers.CacheControl?.NoStore != true && response.Headers.CacheControl?.Private != true
                    && !response.Headers.Vary.Any(value => !value.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase))
                    && (response.Headers.ETag is not null || response.Content.Headers.LastModified is not null);
                await lease.SaveValidatorsAsync(uri.AbsoluteUri, cacheable
                    ? new(response.Headers.ETag?.ToString(), response.Content.Headers.LastModified, body, response.Content.Headers.ContentType?.ToString())
                    : new(null, null), bounded.Token);
            }
        }
        finally
        {
            await lease.SaveAsync(CancellationToken.None);
        }
    }

    /// <summary>Holds the route a paced request acquired, so it is released when the request ends however it ends.</summary>
    private sealed class HeldRoute
    {
        internal IFetchTransportLease? Route { get; set; }
    }

    /// <summary>Holds what a paced request's locked step produced: a refusal to return, or the start it reserved.</summary>
    private sealed record Reservation(HttpResponseMessage? Failure, DateTimeOffset Start = default, ResponseValidators? Validators = null,
        bool SharedCache = false);

    /// <summary>Picks where a request's robots are cached: the host's durable state on a shared route, the route's own slot on a
    /// fixed exit that no session or credential changes (<see cref="IFetchTransportLease.CachesRobotsByRoute"/>), else a slot that
    /// lasts only this fetch.</summary>
    private HostFetchState RobotsState(HttpRequestMessage request, IFetchTransportLease route, bool sharedCache, HostFetchState hostState, string host)
    {
        if (sharedCache)
            return hostState;
        return route.CachesRobotsByRoute && route.Cookies is null
            && !request.Headers.Contains("Authorization") && !request.Headers.Contains("Cookie")
            ? RouteRobots.For(host, route.RouteKey) : new HostFetchState();
    }

    private async Task<(HttpResponseMessage? Failure, bool Loaded)> EnsureRobotsAsync(
        Uri uri, IFetchTransportLease route, IHostFetchLease lease, HostFetchState robotsState, TimeSpan gap, HttpRequestMessage original, FetchUsageCounters usage, CancellationToken cancellationToken)
    {
        string origin = uri.GetLeftPart(UriPartial.Authority);
        TimeSpan lifetime = robotsState.RobotsStatus >= 500 ? TimeSpan.FromMinutes(5) : options.RobotsCacheDuration;
        bool fresh = robotsState.RobotsOrigin == origin && robotsState.RobotsCachedAt is { } cached && cached + lifetime > clock.GetUtcNow();
        if (!fresh)
        {
            Uri target = new(origin + "/robots.txt");
            for (int redirects = 0; ; redirects++)
            {
                using var response = await ReadNetworkAsync(target, route, HttpMethod.Get, null, null, lease, gap, usage, cancellationToken);
                int status = (int)response.StatusCode;
                if (IsRedirect(status))
                {
                    Uri? location = response.Headers.Location;
                    Uri? next = location is null ? null : location.IsAbsoluteUri ? location : new Uri(target, location);
                    if (redirects >= options.MaximumRedirects || !IsSafeRedirect(target, next) || !route.Allows(next!))
                        return (Synthetic(original, FetchOutcome.InvalidRedirect, HttpStatusCode.BadGateway, "Robots redirect left the source host."), false);
                    target = next!;
                    continue;
                }
                byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var verdict = Detect(target, response, body);
                if (verdict is not null && verdict.Outcome != FetchOutcome.Blocked)
                {
                    TimeSpan wait = Pause(lease.State, verdict.Outcome, verdict.RetryAfter);
                    return (Synthetic(original, verdict.Outcome, response.StatusCode, "Robots request was refused.", wait, verdict.Kind), false);
                }

                robotsState.RobotsOrigin = origin;
                robotsState.RobotsStatus = status;
                robotsState.RobotsCachedAt = clock.GetUtcNow();
                robotsState.RobotsBody = status is 401 or 403 ? "User-agent: *\nDisallow: /"
                    : status is >= 400 and < 500 ? string.Empty : Decode(body, response.Content.Headers.ContentType?.CharSet);
                break;
            }
        }

        if (robotsState.RobotsStatus >= 500)
        {
            TimeSpan delay = Pause(lease.State, FetchOutcome.HttpError, TimeSpan.FromMinutes(5));
            return (Synthetic(original, FetchOutcome.HttpError, HttpStatusCode.ServiceUnavailable, "Robots policy is temporarily unavailable.", delay), !fresh);
        }
        return (null, !fresh);
    }

    private async Task<HttpResponseMessage> ReadNetworkAsync(Uri uri, IFetchTransportLease route, HttpMethod method, HttpRequestMessage? original,
        ResponseValidators? validators, IHostFetchLease lease, TimeSpan gap, FetchUsageCounters usage, CancellationToken cancellationToken)
    {
        if (!route.Allows(uri))
            throw new FetchTransportUnavailableException("The configured route does not allow this origin.");
        TimeSpan wait = lease.State.NextAllowedAt - clock.GetUtcNow();
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, clock, cancellationToken);
        using var message = CreateMessage(uri, route, method, original, validators);

        // Advance before connecting so a timed-out or interrupted request also leaves a durable gap.
        lease.State.NextAllowedAt = clock.GetUtcNow() + gap;
        await lease.SaveAsync(cancellationToken);
        HttpResponseMessage response = await SendAndReadAsync(message, uri, route, method, usage, cancellationToken);
        lease.State.NextAllowedAt = clock.GetUtcNow() + gap;
        return response;
    }

    private HttpRequestMessage CreateMessage(Uri uri, IFetchTransportLease route, HttpMethod method, HttpRequestMessage? original,
        ResponseValidators? validators)
    {
        var message = new HttpRequestMessage(method, uri) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
        if (original is not null)
        {
            foreach (var header in original.Headers)
                if (!header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)
                    && !header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
                    && !header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                    message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        message.Headers.UserAgent.ParseAdd(options.UserAgent);
        message.Headers.AcceptEncoding.Clear();
        foreach (string encoding in ContentDecoder.Requested)
            message.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue(encoding));
        if (validators?.ETag is { } etag && !message.Headers.Contains("If-None-Match"))
            message.Headers.TryAddWithoutValidation("If-None-Match", etag);
        if (validators?.LastModified is { } modified && message.Headers.IfModifiedSince is null)
            message.Headers.IfModifiedSince = modified;
        if (route.Cookies?.GetCookieHeader(uri) is { Length: > 0 } cookieHeader)
            message.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        return message;
    }

    private async Task<HttpResponseMessage> SendAndReadAsync(HttpRequestMessage message, Uri uri, IFetchTransportLease route, HttpMethod method,
        FetchUsageCounters usage, CancellationToken cancellationToken)
    {
        usage.Requests++;
        HttpResponseMessage response = await route.Client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            if (route.Cookies is { } sessionCookies && response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                foreach (string value in setCookies)
                {
                    try { sessionCookies.SetCookies(uri, value); }
                    catch (CookieException) { /* A malformed anonymous cookie cannot make a page unreadable. */ }
                }
            if (method != HttpMethod.Head && response.Content.Headers.ContentLength > options.MaximumResponseBytes)
                throw new ResponseTooLargeException();
            using var buffer = new MemoryStream();
            await using var wire = new MeteredStream(await response.Content.ReadAsStreamAsync(cancellationToken), usage);
            // A reply without a body (HEAD, 204, 205, 304) can name an encoding it does not use.
            bool hasBody = method != HttpMethod.Head
                && response.StatusCode is not (HttpStatusCode.NoContent or HttpStatusCode.ResetContent or HttpStatusCode.NotModified);
            ContentDecoder? decoder = null;
            if (hasBody && !ContentDecoder.TryOpen(response.Content.Headers, wire, out decoder))
                throw new ResponseDecodeException();
            Stream source = decoder?.Stream ?? wire;
            byte[] chunk = new byte[8192];
            try
            {
                while (true)
                {
                    int read;
                    try
                    {
                        read = await source.ReadAsync(chunk, cancellationToken);
                    }
                    catch (Exception error) when (decoder is not null && (error is InvalidDataException
                        || error is InvalidOperationException and not ObjectDisposedException))
                    {
                        throw new ResponseDecodeException();
                    }
                    if (read == 0)
                        break;
                    // The limit is on decoded bytes, so a small body that inflates past it stops here.
                    if (buffer.Length + read > options.MaximumResponseBytes)
                        throw new ResponseTooLargeException();
                    buffer.Write(chunk, 0, read);
                }
                if (decoder is not null)
                {
                    await DrainAsync(wire, chunk, cancellationToken);
                    if (!decoder.IsComplete(buffer.Length))
                        throw new ResponseDecodeException();
                }
            }
            finally
            {
                if (decoder is not null)
                    await decoder.DisposeAsync();
            }
            var content = new ByteArrayContent(buffer.ToArray());
            foreach (var header in response.Content.Headers)
                if (decoder is null || !header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase)
                    && !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content.Dispose();
            response.Content = content;
            response.RequestMessage = message;
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>Reads what a decoder left on the wire, so every wire byte is metered and a pooled connection is reusable; a
    /// stream that goes on past the response limit is left for the connection's disposal.</summary>
    private async Task DrainAsync(MeteredStream wire, byte[] sink, CancellationToken cancellationToken)
    {
        long drained = 0;
        while (drained <= options.MaximumResponseBytes)
        {
            int read = await wire.ReadAsync(sink, cancellationToken);
            if (read == 0)
                break;
            drained += read;
        }
    }

    private ChallengeVerdict? Detect(Uri uri, HttpResponseMessage response, byte[] body) => challenges.Detect(new ChallengeProbe
    {
        RequestUri = uri,
        FinalUri = uri,
        StatusCode = (int)response.StatusCode,
        ContentType = response.Content.Headers.ContentType?.MediaType,
        Headers = Headers(response),
        Body = Decode(body.AsSpan(0, Math.Min(body.Length, 65536)).ToArray(), response.Content.Headers.ContentType?.CharSet)
    });

    private TimeSpan Pause(HostFetchState state, FetchOutcome outcome, TimeSpan? retryAfter)
    {
        state.ConsecutiveFailures++;
        double factor = Math.Pow(2, Math.Min(state.ConsecutiveFailures - 1, 10));
        TimeSpan delay = TimeSpan.FromSeconds(Math.Min(options.RefusalBackoff.TotalSeconds * factor, 3600));
        if (retryAfter is { } requested && requested > delay)
            delay = requested;
        state.PausedUntil = clock.GetUtcNow() + delay;
        state.PauseReason = outcome.ToString();
        if (retryAfter is { } wait)
            state.RetryAfterUntil = clock.GetUtcNow() + wait;
        state.AdaptiveDelayMilliseconds = (int)Math.Min(Math.Max(state.AdaptiveDelayMilliseconds * 2L, (long)options.MinimumDelay.TotalMilliseconds), 30000);
        return delay;
    }

    private static bool IsWebUri(Uri? uri) => uri is { IsAbsoluteUri: true }
        && uri.UserInfo.Length == 0 && uri.Scheme is "http" or "https";

    private static bool IsSafeRedirect(Uri from, Uri? to) => IsWebUri(to)
        && string.Equals(from.IdnHost.TrimEnd('.'), to!.IdnHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase)
        && (from.Scheme != Uri.UriSchemeHttps || to.Scheme == Uri.UriSchemeHttps);

    private static bool IsRedirect(int status) => status is 301 or 302 or 303 or 307 or 308;

    private static bool BlockedDestination(Exception error)
    {
        for (Exception? cause = error; cause is not null; cause = cause.InnerException)
            if (cause is OutboundAddressBlockedException)
                return true;
        return false;
    }

    private static DateTimeOffset? Later(DateTimeOffset? left, DateTimeOffset? right) =>
        left is null ? right : right is null || left >= right ? left : right;

    private static IReadOnlyList<KeyValuePair<string, string>> Headers(HttpResponseMessage response) => response.Headers.Concat(response.Content.Headers)
        .SelectMany(header => header.Value.Select(value => new KeyValuePair<string, string>(header.Key, value))).ToArray();

    private static string Decode(byte[] body, string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { return Encoding.GetEncoding(charset.Trim('"')).GetString(body); }
            catch (ArgumentException) { /* Unknown charset: use the web's UTF-8 default. */ }
        }
        return Encoding.UTF8.GetString(body);
    }

    private static HttpResponseMessage Synthetic(HttpRequestMessage request, FetchOutcome outcome, HttpStatusCode status,
        string message, TimeSpan? retryAfter = null, ChallengeKind? challenge = null)
    {
        request.Options.Set(HarvestFetchMetadata.ResponseKey, new(outcome, challenge, retryAfter, message));
        var response = new HttpResponseMessage(status) { RequestMessage = request, Content = new ByteArrayContent([]) };
        if (retryAfter is { } delay)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(delay);
        return response;
    }

    private const string DecodeMessage = "The response used a content encoding Harvest cannot decode, or its encoded body was corrupt.";

    private sealed class ResponseTooLargeException : Exception;

    private sealed class ResponseDecodeException : Exception;
}
