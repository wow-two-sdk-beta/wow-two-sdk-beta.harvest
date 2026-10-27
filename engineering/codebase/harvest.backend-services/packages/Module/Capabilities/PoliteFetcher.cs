using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Pacing;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Http.Safety;
using RobotsFileModel = WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Robots.RobotsFile;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Fetches politely and directly: it paces each host, obeys the source's robots policy, sends conditional
/// requests, honours <c>Retry-After</c>, tells a challenge from a block from a rate limit, and records the host's
/// back-off.</summary>
/// <remarks>Direct fetching only. A challenge, a block or a rate limit is recorded and backs the host off; nothing is
/// retried through another route — no proxy, no rotation, no disguised client, no solver, no sign-in.</remarks>
internal sealed class PoliteFetcher(
    IFetchClientFactory clients,
    IEnumerable<IChallengeDetector> detectors,
    HostStateStore store,
    IOptions<HarvestModuleOptions> options,
    TimeProvider time) : IPoliteFetcher
{
    private const int ProbeBodyBytes = 64 * 1024;

    private static readonly int[] RedirectStatuses = [301, 302, 303, 307, 308];

    private readonly IReadOnlyList<IChallengeDetector> detectors = [.. detectors];
    private readonly FetchingOptions settings = options.Value.Fetching;

    /// <inheritdoc />
    public async Task<FetchGate> ReserveAsync(string hostKey, string lane = "default", CancellationToken cancellationToken = default)
    {
        var now = Now();
        var host = await store.PeekHostAsync(hostKey, cancellationToken);
        if (host is null)
            return new FetchGate(true, now, TimeSpan.Zero);

        var laneRow = await store.PeekLaneAsync(hostKey, lane, cancellationToken);
        var paused = Later(host.PausedUntil, laneRow?.PausedUntil);
        var due = HostPacing.DueAt(host.NextAllowedAt, paused, host.RetryAfterUntil);
        return due <= now ? new FetchGate(true, now, TimeSpan.Zero) : new FetchGate(false, due, due - now);
    }

    /// <inheritdoc />
    public async Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = Now();
        var host = await store.HostAsync(request.HostKey, now, cancellationToken);
        var token = ProductToken(settings.UserAgent);

        using var client = clients.CreateClient(request.Cookies);

        TimeSpan? crawlDelay = null;
        if (request.ObeyRobots)
        {
            var robots = await RobotsAsync(client, host, request.Url, now, cancellationToken);
            crawlDelay = robots.CrawlDelay(token);
            if (!robots.IsAllowed(request.Url.PathAndQuery, token))
                return await RecordAsync(host, request, now, crawlDelay, new Attempt(FetchOutcome.RobotsDisallowed, Evidence: "robots.txt disallow"), cancellationToken);
        }

        var attempt = await SendAsync(client, request, now, cancellationToken);
        return await RecordAsync(host, request, now, crawlDelay, attempt, cancellationToken);
    }

    /// <summary>Sends the request, following safe redirects, reads the answer and classifies it.</summary>
    private async Task<Attempt> SendAsync(HttpClient client, FetchRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var url = request.Url;
        for (var redirect = 0; ; redirect++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            if (redirect == 0)
                AddConditionalHeaders(message, request);

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new Attempt(FetchOutcome.FetchTimeout, Evidence: "fetch timed out");
            }
            catch (OutboundAddressBlockedException exception)
            {
                return new Attempt(FetchOutcome.InvalidRedirect, Evidence: exception.Message);
            }
            catch (Exception exception) when (exception is HttpRequestException or SocketException or IOException)
            {
                return new Attempt(FetchOutcome.NetworkError, Evidence: exception.GetType().Name);
            }

            try
            {
                var status = (int)response.StatusCode;
                if (Array.IndexOf(RedirectStatuses, status) >= 0 && response.Headers.Location is { } location)
                {
                    if (redirect >= settings.MaxRedirects)
                        return new Attempt(FetchOutcome.InvalidRedirect, Evidence: "too many redirects");

                    var next = new Uri(url, location);
                    if (next.Scheme != Uri.UriSchemeHttps && next.Scheme != Uri.UriSchemeHttp)
                        return new Attempt(FetchOutcome.InvalidRedirect, Evidence: $"unsafe redirect scheme '{next.Scheme}'");
                    if (settings.RequireHttps && next.Scheme != Uri.UriSchemeHttps)
                        return new Attempt(FetchOutcome.InvalidRedirect, Evidence: "redirect downgraded to http");

                    url = next;
                    continue;
                }

                return await ClassifyAsync(request.Url, url, response, now, cancellationToken);
            }
            finally
            {
                response.Dispose();
            }
        }
    }

    /// <summary>Reads the final response and names its outcome — a challenge, a block, a rate limit, a 304, content or a
    /// plain error.</summary>
    private async Task<Attempt> ClassifyAsync(Uri requestUrl, Uri finalUrl, HttpResponseMessage response, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var headers = CollectHeaders(response);
        var retryAfter = ResolveRetryAfter(response, now);
        var etag = response.Headers.ETag?.Tag;
        var lastModified = response.Content.Headers.LastModified;

        if (status == 304)
            return new Attempt(FetchOutcome.NotModified, StatusCode: status, ETag: etag, LastModifiedUtc: lastModified, RetryAfter: retryAfter);

        var (body, tooLarge) = await ReadBodyAsync(response, cancellationToken);
        if (tooLarge)
            return new Attempt(FetchOutcome.ResponseTooLarge, StatusCode: status, Evidence: $"over {settings.MaxResponseBytes} bytes");

        var probe = new ChallengeProbe
        {
            RequestUri = requestUrl,
            FinalUri = finalUrl,
            StatusCode = status,
            ContentType = response.Content.Headers.ContentType?.MediaType,
            Headers = headers,
            Body = DecodePrefix(body, response.Content.Headers.ContentType?.CharSet),
        };

        if (Classify(probe) is { } verdict)
            return new Attempt(verdict.Outcome, StatusCode: status, RetryAfter: retryAfter ?? verdict.RetryAfter, Challenge: verdict, Evidence: verdict.Evidence);

        var outcome = status switch
        {
            >= 200 and < 300 => FetchOutcome.Ok,
            404 or 410 => FetchOutcome.Gone,
            429 => FetchOutcome.RateLimited,
            401 => FetchOutcome.LoginRequired,
            403 or 451 => FetchOutcome.Blocked,
            >= 300 and < 400 => FetchOutcome.InvalidRedirect,
            _ => FetchOutcome.HttpError,
        };

        FetchedDocument? document = outcome == FetchOutcome.Ok
            ? new FetchedDocument
            {
                RequestUrl = requestUrl,
                FinalUrl = finalUrl,
                StatusCode = status,
                ContentType = response.Content.Headers.ContentType?.MediaType,
                Charset = response.Content.Headers.ContentType?.CharSet,
                Headers = headers,
                Body = body,
                FetchedAtUtc = now,
            }
            : null;

        return new Attempt(outcome, StatusCode: status, Document: document, ETag: etag, LastModifiedUtc: lastModified, RetryAfter: retryAfter, Evidence: $"{status}");
    }

    /// <summary>Fetches and caches the host's <c>robots.txt</c>, or reads the cached copy while it is fresh; a missing or
    /// unreadable file allows everything.</summary>
    private async Task<RobotsFileModel> RobotsAsync(HttpClient client, HostStateRow host, Uri url, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (host.RobotsCachedAt is { } cachedAt && now - cachedAt < settings.RobotsCacheTtl)
            return host is { RobotsStatus: 200, RobotsBody: { } cached } ? RobotsFileModel.Parse(cached) : RobotsFileModel.AllowAll;

        var robotsUrl = new Uri(url.GetLeftPart(UriPartial.Authority) + "/robots.txt");
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, robotsUrl);
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = (int)response.StatusCode;
            host.RobotsStatus = (short)status;
            host.RobotsCachedAt = now;
            if (status is >= 200 and < 300)
            {
                var (body, _) = await ReadBodyAsync(response, cancellationToken, settings.RobotsMaxBytes);
                var text = DecodePrefix(body, response.Content.Headers.ContentType?.CharSet, body.Length);
                host.RobotsBody = text;
                return RobotsFileModel.Parse(text);
            }

            host.RobotsBody = null;
            return RobotsFileModel.AllowAll;
        }
        catch (Exception exception) when (exception is HttpRequestException or SocketException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // An unreachable robots.txt does not stall harvesting; treat it as absent until the cache refreshes.
            host.RobotsStatus = null;
            host.RobotsBody = null;
            host.RobotsCachedAt = now;
            return RobotsFileModel.AllowAll;
        }
    }

    /// <summary>Applies the pacing decision to the host and lane rows, writes them, and builds the result.</summary>
    private async Task<FetchResult> RecordAsync(HostStateRow host, FetchRequest request, DateTimeOffset now, TimeSpan? crawlDelay, Attempt attempt, CancellationToken cancellationToken)
    {
        var decision = HostPacing.Plan(
            attempt.Outcome,
            TimeSpan.FromMilliseconds(host.AdaptiveDelayMs),
            host.ConsecutiveFailures,
            crawlDelay,
            request.StepMinDelay,
            attempt.RetryAfter,
            now,
            settings);

        var lane = await store.LaneAsync(request.HostKey, request.Lane, now, cancellationToken);

        host.AdaptiveDelayMs = (int)decision.AdaptiveDelay.TotalMilliseconds;
        host.CrawlDelayS = HostPacing.ClampCrawlDelay(crawlDelay) is { } clamped ? (int)clamped.TotalSeconds : null;
        host.NextAllowedAt = decision.NextAllowedAtUtc;
        host.RetryAfterUntil = decision.RetryAfterUntilUtc;
        host.PausedUntil = decision.PausedUntilUtc;
        host.PauseReason = decision.PauseReason;
        host.ConsecutiveFailures = decision.ConsecutiveFailures;
        host.UpdatedAt = now;

        lane.ConsecutiveFailures = decision.ConsecutiveFailures;
        lane.PausedUntil = decision.PausedUntilUtc;
        if (attempt.Challenge?.Kind is { } kind)
            lane.LastChallengeKind = SnakeCaseEnum<ChallengeKind>.ToText(kind);
        lane.UpdatedAt = now;

        await store.SaveAsync(cancellationToken);

        return new FetchResult
        {
            Outcome = attempt.Outcome,
            StatusCode = attempt.StatusCode,
            Document = attempt.Document,
            ETag = attempt.ETag,
            LastModifiedUtc = attempt.LastModifiedUtc,
            RetryAfter = attempt.RetryAfter,
            Challenge = attempt.Challenge,
            Evidence = attempt.Evidence,
            NextAllowedAtUtc = decision.NextAllowedAtUtc,
            PausedUntilUtc = decision.PausedUntilUtc,
        };
    }

    /// <summary>Runs the detectors in order; the first verdict wins, so a host-specific adapter's detector comes first.</summary>
    private ChallengeVerdict? Classify(ChallengeProbe probe)
    {
        foreach (var detector in detectors)
            if (detector.Detect(probe) is { } verdict)
                return verdict;
        return null;
    }

    private static void AddConditionalHeaders(HttpRequestMessage message, FetchRequest request)
    {
        if (!string.IsNullOrEmpty(request.ETag))
            message.Headers.TryAddWithoutValidation("If-None-Match", request.ETag);
        if (request.LastModifiedUtc is { } since)
            message.Headers.IfModifiedSince = since;
    }

    private static TimeSpan? ResolveRetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        var header = response.Headers.RetryAfter;
        if (header is null)
            return null;
        if (header.Delta is { } delta)
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (header.Date is { } date)
            return date <= now ? TimeSpan.Zero : date - now;
        return null;
    }

    private async Task<(ReadOnlyMemory<byte> Body, bool TooLarge)> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken, long? limit = null)
    {
        var max = limit ?? settings.MaxResponseBytes;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > max)
            {
                if (limit is null)
                    return (ReadOnlyMemory<byte>.Empty, true);
                buffer.Write(chunk, 0, (int)(max - buffer.Length));
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        return (buffer.ToArray(), false);
    }

    private static string DecodePrefix(ReadOnlyMemory<byte> body, string? charset, int? limit = null)
    {
        if (body.Length == 0)
            return string.Empty;
        var length = Math.Min(body.Length, limit ?? ProbeBodyBytes);
        var encoding = ResolveEncoding(charset);
        return encoding.GetString(body.Span[..length]);
    }

    private static Encoding ResolveEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
            return Encoding.UTF8;
        try
        {
            return Encoding.GetEncoding(charset.Trim().Trim('"'));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    private static IReadOnlyList<KeyValuePair<string, string>> CollectHeaders(HttpResponseMessage response)
    {
        var headers = new List<KeyValuePair<string, string>>();
        foreach (var header in response.Headers)
            headers.Add(new(header.Key, string.Join(", ", header.Value)));
        foreach (var header in response.Content.Headers)
            headers.Add(new(header.Key, string.Join(", ", header.Value)));
        return headers;
    }

    private static string ProductToken(string userAgent)
    {
        var end = userAgent.AsSpan().IndexOfAny('/', ' ');
        var token = end > 0 ? userAgent[..end] : userAgent;
        return token.ToLowerInvariant();
    }

    private static DateTimeOffset? Later(DateTimeOffset? first, DateTimeOffset? second) =>
        (first, second) switch
        {
            ({ } a, { } b) => a > b ? a : b,
            ({ } a, null) => a,
            (null, { } b) => b,
            _ => null,
        };

    /// <summary>Reads the clock at PostgreSQL's microsecond precision, so a written row reads back unchanged.</summary>
    private DateTimeOffset Now()
    {
        var value = time.GetUtcNow();
        return value.AddTicks(-(value.Ticks % 10));
    }

    /// <summary>What a fetch attempt came back as, before the pacing decision is folded in.</summary>
    private sealed record Attempt(
        FetchOutcome Outcome,
        int? StatusCode = null,
        FetchedDocument? Document = null,
        string? ETag = null,
        DateTimeOffset? LastModifiedUtc = null,
        TimeSpan? RetryAfter = null,
        ChallengeVerdict? Challenge = null,
        string? Evidence = null);
}
