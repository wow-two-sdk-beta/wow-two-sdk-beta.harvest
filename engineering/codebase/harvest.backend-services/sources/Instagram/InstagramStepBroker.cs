using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Enumerates approved profile media. Continuation targets are internal work identifiers, never fetched web URLs.</summary>
public sealed class InstagramStepBroker(IInstagramMediaClient client, IOptions<InstagramOptions> options) : IStepBroker
{
    /// <inheritdoc />
    public string AdapterKey => InstagramSourceAdapter.AdapterKey;
    /// <inheritdoc />
    public string StepKey => "enumerate";
    /// <inheritdoc />
    public async Task<StepResult> ExecuteAsync(StepItem item, IStepContext context, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(item.Target, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0 || uri.IdnHost is not ("www.instagram.com" or "instagram.com"))
            return new() { Outcome = FetchOutcome.NotApproved, Message = "Use an HTTPS Instagram publisher profile URL." };
        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 1 || !InstagramBusinessDiscoveryClient.Username().IsMatch(segments[0])
            || segments[0] is "p" or "reel" or "explore" or "stories" or "accounts")
            return new() { Outcome = FetchOutcome.Unrecognized, Message = "A publisher profile URL is required, rather than a post, hashtag or story URL." };
        string username = segments[0].ToLowerInvariant();
        string profile = $"https://{uri.IdnHost}/{username}/";
        string? after = null;
        int pageNumber = 0;
        if (item.Payload is { ValueKind: JsonValueKind.Object } payload)
        {
            after = InstagramBusinessDiscoveryClient.Text(payload, "after");
            if (!payload.TryGetProperty("page", out var number) || number.ValueKind != JsonValueKind.Number
                || !number.TryGetInt32(out pageNumber) || pageNumber < 1 || pageNumber >= options.Value.MaximumPages || after is null)
                return new() { Outcome = FetchOutcome.UnexpectedContent, Message = "Invalid Instagram continuation state." };
        }
        InstagramMediaPage result = await client.ReadAsync(username, after, cancellationToken);
        context.Observe(new() { Outcome = result.Outcome, Bytes = result.Usage.Bytes, RetryAfter = result.RetryAfter });
        if (result.Outcome != FetchOutcome.Ok)
            return new() { Outcome = result.Outcome, Usage = result.Usage, RetryAfter = result.RetryAfter, Message = result.Message };
        Capture[] captures = [.. result.Media.Select(record => InstagramCaptureReader.Read(record, username, profile, context.Clock.GetUtcNow()))
            .OfType<Capture>().Select(capture => capture with
            {
                RequestUrl = result.RequestUrl ?? profile, FinalUrl = result.RequestUrl ?? profile, HttpStatus = result.HttpStatus,
            })];
        bool truncated = result.After is not null && pageNumber + 1 >= options.Value.MaximumPages;
        StepTarget[] next = [];
        if (result.After is { } cursor && !truncated)
        {
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cursor)))[..24].ToLowerInvariant();
            next = [new()
            {
                StepKey = StepKey, Target = profile + "?__harvest_cursor=" + digest,
                Payload = JsonSerializer.SerializeToElement(new { after = cursor, page = pageNumber + 1 }),
            }];
        }
        return new()
        {
            Outcome = captures.Length == 0 && result.Media.Count > 0 ? FetchOutcome.Unrecognized : FetchOutcome.Ok,
            Captures = captures, Next = next, Usage = result.Usage, EnumerationComplete = false,
            Message = truncated ? "Configured recent-media page limit reached; older posts were not scanned."
                : captures.Length != result.Media.Count ? "Some malformed media records were skipped; review the source response." : null,
        };
    }
}
