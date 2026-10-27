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
internal sealed class HarvestFetchHandler(
    HttpClient transport,
    IHostFetchStore hosts,
    HarvestFetchOptions options,
    IChallengeDetector challenges,
    TimeProvider clock,
    CookieContainer? cookies = null) : HttpMessageHandler
{
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
                    var robots = await EnsureRobotsAsync(uri, lease, gap, request, usage, cancellationToken);
                    if (robots.Failure is { } failure)
                        return failure;
                    var rules = RobotsRules.Parse(lease.State.RobotsBody ?? string.Empty, options.UserAgent);
                    lease.State.CrawlDelaySeconds = rules.CrawlDelay is { } parsedDelay ? (int)Math.Ceiling(parsedDelay.TotalSeconds) : null;
                    if (rules.CrawlDelay is { } crawlDelay && crawlDelay > gap)
                        gap = crawlDelay;
                    if (!rules.Allows(uri))
                        return Synthetic(request, FetchOutcome.RobotsDisallowed, HttpStatusCode.Forbidden, "Host robots policy disallows this path.");
                }

                bool sharedCache = cookies is null && !request.Headers.Contains("Authorization") && !request.Headers.Contains("Cookie");
                var validators = sharedCache ? await lease.GetValidatorsAsync(uri.AbsoluteUri, cancellationToken) : null;
                HttpResponseMessage response = await ReadNetworkAsync(uri, request.Method, request, validators, lease, gap, usage, cancellationToken);
                int status = (int)response.StatusCode;
                if (IsRedirect(status))
                {
                    Uri? location = response.Headers.Location;
                    Uri? next = location is null ? null : location.IsAbsoluteUri ? location : new Uri(uri, location);
                    if (redirects >= options.MaximumRedirects || !IsSafeRedirect(uri, next))
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
                    if (response.IsSuccessStatusCode && request.Method == HttpMethod.Get && sharedCache)
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
        catch (HttpRequestException error) when (BlockedDestination(error))
        {
            return Synthetic(request, FetchOutcome.InvalidRedirect, HttpStatusCode.BadRequest, "Destination failed the public-network policy.");
        }
        catch (ResponseTooLargeException)
        {
            Pause(lease.State, FetchOutcome.ResponseTooLarge, null);
            return Synthetic(request, FetchOutcome.ResponseTooLarge, HttpStatusCode.RequestEntityTooLarge, "Response exceeded the configured byte limit.");
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

    private async Task<(HttpResponseMessage? Failure, bool Loaded)> EnsureRobotsAsync(
        Uri uri, IHostFetchLease lease, TimeSpan gap, HttpRequestMessage original, FetchUsageCounters usage, CancellationToken cancellationToken)
    {
        string origin = uri.GetLeftPart(UriPartial.Authority);
        TimeSpan lifetime = lease.State.RobotsStatus >= 500 ? TimeSpan.FromMinutes(5) : options.RobotsCacheDuration;
        bool fresh = lease.State.RobotsOrigin == origin && lease.State.RobotsCachedAt is { } cached && cached + lifetime > clock.GetUtcNow();
        if (!fresh)
        {
            Uri target = new(origin + "/robots.txt");
            for (int redirects = 0; ; redirects++)
            {
                using var response = await ReadNetworkAsync(target, HttpMethod.Get, null, null, lease, gap, usage, cancellationToken);
                int status = (int)response.StatusCode;
                if (IsRedirect(status))
                {
                    Uri? location = response.Headers.Location;
                    Uri? next = location is null ? null : location.IsAbsoluteUri ? location : new Uri(target, location);
                    if (redirects >= options.MaximumRedirects || !IsSafeRedirect(target, next))
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

                lease.State.RobotsOrigin = origin;
                lease.State.RobotsStatus = status;
                lease.State.RobotsCachedAt = clock.GetUtcNow();
                lease.State.RobotsBody = status is 401 or 403 ? "User-agent: *\nDisallow: /"
                    : status is >= 400 and < 500 ? string.Empty : Decode(body, response.Content.Headers.ContentType?.CharSet);
                break;
            }
        }

        if (lease.State.RobotsStatus >= 500)
        {
            TimeSpan delay = Pause(lease.State, FetchOutcome.HttpError, TimeSpan.FromMinutes(5));
            return (Synthetic(original, FetchOutcome.HttpError, HttpStatusCode.ServiceUnavailable, "Robots policy is temporarily unavailable.", delay), !fresh);
        }
        return (null, !fresh);
    }

    private async Task<HttpResponseMessage> ReadNetworkAsync(Uri uri, HttpMethod method, HttpRequestMessage? original,
        ResponseValidators? validators, IHostFetchLease lease, TimeSpan gap, FetchUsageCounters usage, CancellationToken cancellationToken)
    {
        TimeSpan wait = lease.State.NextAllowedAt - clock.GetUtcNow();
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, clock, cancellationToken);
        using var message = new HttpRequestMessage(method, uri) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
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
        if (validators?.ETag is { } etag && !message.Headers.Contains("If-None-Match"))
            message.Headers.TryAddWithoutValidation("If-None-Match", etag);
        if (validators?.LastModified is { } modified && message.Headers.IfModifiedSince is null)
            message.Headers.IfModifiedSince = modified;
        if (cookies?.GetCookieHeader(uri) is { Length: > 0 } cookieHeader)
            message.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

        // Advance before connecting so a timed-out or interrupted request also leaves a durable gap.
        lease.State.NextAllowedAt = clock.GetUtcNow() + gap;
        await lease.SaveAsync(cancellationToken);
        usage.Requests++;
        HttpResponseMessage response = await transport.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            if (cookies is not null && response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                foreach (string value in setCookies)
                {
                    try { cookies.SetCookies(uri, value); }
                    catch (CookieException) { /* A malformed anonymous cookie cannot make a page unreadable. */ }
                }
            if (method != HttpMethod.Head && response.Content.Headers.ContentLength > options.MaximumResponseBytes)
                throw new ResponseTooLargeException();
            using var buffer = new MemoryStream();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            byte[] chunk = new byte[8192];
            while (true)
            {
                int read = await stream.ReadAsync(chunk, cancellationToken);
                if (read == 0)
                    break;
                usage.Bytes += read;
                if (buffer.Length + read > options.MaximumResponseBytes)
                    throw new ResponseTooLargeException();
                buffer.Write(chunk, 0, read);
            }
            var content = new ByteArrayContent(buffer.ToArray());
            foreach (var header in response.Content.Headers)
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content.Dispose();
            response.Content = content;
            response.RequestMessage = message;
            lease.State.NextAllowedAt = clock.GetUtcNow() + gap;
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            transport.Dispose();
        base.Dispose(disposing);
    }

    private sealed class ResponseTooLargeException : Exception;
}
