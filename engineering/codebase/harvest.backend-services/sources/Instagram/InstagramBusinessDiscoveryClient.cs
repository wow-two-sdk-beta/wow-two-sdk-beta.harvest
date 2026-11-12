using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Calls one fixed HTTPS origin with header-only credentials, bounded JSON and no automatic retries.</summary>
public sealed partial class InstagramBusinessDiscoveryClient(
    IHttpClientFactory clients, IOptions<InstagramOptions> configuration, TimeProvider clock) : IInstagramMediaClient
{
    /// <summary>The isolated guarded provider client.</summary>
    public const string ClientName = "Harvest.Instagram.BusinessDiscovery";

    /// <inheritdoc />
    public async Task<InstagramMediaPage> ReadAsync(string username, string? after = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InstagramOptions options = configuration.Value;
        if (!options.Enabled)
            return Failure(FetchOutcome.CapabilityUnavailable, "Instagram is disabled. Configure HarvestInstagram and enable it explicitly.");
        if (!HasValidConfiguration(options))
            return Failure(FetchOutcome.CapabilityUnavailable, "Instagram needs a supported API version, professional account ID, token and valid request bounds.");
        if (!Username().IsMatch(username) || options.AllowedUsernames is null
            || !options.AllowedUsernames.Contains(username, StringComparer.OrdinalIgnoreCase))
            return Failure(FetchOutcome.NotApproved, "The Instagram publisher is not in HarvestInstagram:AllowedUsernames.");
        if (after is not null && !Cursor().IsMatch(after))
            return Failure(FetchOutcome.UnexpectedContent, "Instagram returned an unsupported pagination cursor.");

        string media = $"media.limit({options.PageSize})" + (after is null ? "" : $".after({after})");
        string fields = $"business_discovery.username({username}){{username,{media}{{id,caption,timestamp,permalink,media_type,media_url,thumbnail_url,children{{id,media_type,media_url,thumbnail_url}}}}}}";
        var uri = new Uri($"https://graph.facebook.com/{options.ApiVersion}/{options.ProfessionalAccountId}?fields={Uri.EscapeDataString(fields)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Timeout);
        long bytes = 0;
        try
        {
            using HttpClient http = clients.CreateClient(ClientName);
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var retry = response.Headers.RetryAfter?.Delta;
            if (response.Headers.RetryAfter?.Date is { } until)
                retry = until - clock.GetUtcNow();
            if (retry < TimeSpan.Zero) retry = TimeSpan.Zero;
            if ((int)response.StatusCode is >= 300 and < 400)
                return Failure(FetchOutcome.InvalidRedirect, "Instagram API redirects are not followed.", 1);
            if (response.Content.Headers.ContentLength > options.MaximumResponseBytes)
                return Failure(FetchOutcome.ResponseTooLarge, "Instagram response exceeded the configured byte limit.", 1);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            byte[] block = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(block, deadline.Token)) > 0)
            {
                bytes += count;
                if (bytes > options.MaximumResponseBytes)
                    return Failure(FetchOutcome.ResponseTooLarge, "Instagram response exceeded the configured byte limit.", 1, bytes);
                buffer.Write(block, 0, count);
            }
            // Status takes precedence when a throttle or access failure has no valid JSON body.
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return Failure(FetchOutcome.RateLimited, "Instagram rate limit; wait before another run.", 1, bytes, retry ?? TimeSpan.FromMinutes(15));
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return Failure(FetchOutcome.LoginRequired, "Instagram credentials need renewal.", 1, bytes);
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return Failure(FetchOutcome.Restricted, "Instagram permissions or account eligibility do not allow this request.", 1, bytes);
            using JsonDocument document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Failure(FetchOutcome.UnexpectedContent, "Instagram did not return an object.", 1, bytes);
            if (root.TryGetProperty("error", out JsonElement error))
            {
                int code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var value)
                    && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int parsed) ? parsed : 0;
                FetchOutcome outcome = code switch
                {
                    190 => FetchOutcome.LoginRequired,
                    4 or 17 or 32 or 613 or 80004 => FetchOutcome.RateLimited,
                    10 or 100 or 200 => FetchOutcome.Restricted,
                    _ => FetchOutcome.ProviderFailed,
                };
                return Failure(outcome, $"Instagram API error {code}; check credentials, permissions and publisher eligibility.",
                    1, bytes, outcome == FetchOutcome.RateLimited ? retry ?? TimeSpan.FromMinutes(15) : null);
            }
            if (!response.IsSuccessStatusCode)
                return Failure(FetchOutcome.HttpError, $"Instagram API HTTP {(int)response.StatusCode}.", 1, bytes);
            if (!root.TryGetProperty("business_discovery", out JsonElement discovery) || discovery.ValueKind != JsonValueKind.Object
                || Text(discovery, "username") is not { } returned || !returned.Equals(username, StringComparison.OrdinalIgnoreCase)
                || !discovery.TryGetProperty("media", out JsonElement page) || page.ValueKind != JsonValueKind.Object
                || !page.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > options.PageSize)
                return Failure(FetchOutcome.Restricted, "Instagram did not return the requested professional publisher's media. Check eligibility and permissions.", 1, bytes);
            string? cursor = null;
            if (page.TryGetProperty("paging", out JsonElement paging) && paging.ValueKind == JsonValueKind.Object
                && Text(paging, "next") is not null)
            {
                cursor = paging.TryGetProperty("cursors", out JsonElement cursors) ? Text(cursors, "after") : null;
                if (cursor is null || !Cursor().IsMatch(cursor) || cursor == after)
                    return Failure(FetchOutcome.UnexpectedContent, "Instagram pagination did not advance safely.", 1, bytes);
            }
            return new()
            {
                Outcome = FetchOutcome.Ok, Media = [.. data.EnumerateArray().Select(item => item.Clone())],
                After = cursor, RequestUrl = uri.AbsoluteUri, HttpStatus = (int)response.StatusCode,
                Usage = new() { Requests = 1, Bytes = bytes },
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Failure(FetchOutcome.FetchTimeout, "Instagram API request timed out.", 1, bytes); }
        catch (HttpRequestException)
        { return Failure(FetchOutcome.NetworkError, "Instagram API connection failed.", 1, bytes); }
        catch (JsonException)
        { return Failure(FetchOutcome.UnexpectedContent, "Instagram did not return valid bounded JSON.", 1, bytes); }
    }

    internal static bool HasValidConfiguration(InstagramOptions options) => options.Enabled
        && !string.IsNullOrWhiteSpace(options.AccessToken) && options.AccessToken.Length <= 8192
        && options.AccessToken.All(value => value is > (char)32 and < (char)127)
        && NumericId().IsMatch(options.ProfessionalAccountId ?? string.Empty) && Version().IsMatch(options.ApiVersion ?? string.Empty)
        && options.PageSize is >= 1 and <= 50 && options.MaximumPages is >= 1 and <= 20
        && options.MaximumResponseBytes is >= 1024 and <= 8_388_608
        && options.Timeout > TimeSpan.Zero && options.Timeout <= TimeSpan.FromMinutes(1);

    internal static string? Text(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static InstagramMediaPage Failure(FetchOutcome outcome, string message, long requests = 0, long bytes = 0, TimeSpan? retry = null) =>
        new() { Outcome = outcome, Message = message, Usage = new() { Requests = requests, Bytes = bytes }, RetryAfter = retry };
    [GeneratedRegex(@"^[a-zA-Z0-9_.]{1,30}$", RegexOptions.CultureInvariant)]
    internal static partial Regex Username();
    [GeneratedRegex(@"^[0-9]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericId();
    [GeneratedRegex(@"^v[0-9]{1,3}\.0$", RegexOptions.CultureInvariant)]
    private static partial Regex Version();
    [GeneratedRegex(@"^[a-zA-Z0-9_+=/\-]{1,4096}$", RegexOptions.CultureInvariant)]
    private static partial Regex Cursor();
}
