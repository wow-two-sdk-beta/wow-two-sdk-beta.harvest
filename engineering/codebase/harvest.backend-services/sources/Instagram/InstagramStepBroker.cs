using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Enumerates approved profile media. Continuation targets are internal work identifiers, never fetched web URLs.</summary>
public sealed class InstagramStepBroker(IInstagramMediaBroker broker) : IStepBroker
{
    /// <inheritdoc />
    public string AdapterKey => InstagramSourceAdapter.AdapterKey;
    /// <inheritdoc />
    public string StepKey => "enumerate";
    /// <inheritdoc />
    public IReadOnlyList<string> OperationKeys => ["prepare-authentication", "fetch", "map-response", "paginate"];
    /// <inheritdoc />
    public async Task<StepResult> ExecuteAsync(StepItem item, IStepContext context, CancellationToken cancellationToken = default)
    {
        if (!InstagramPublishers.TryReadProfile(context.Scope.Url, out string username, out string profile)
            || !InstagramPublishers.MatchesSource(context.Source.Handle, profile)
            || context.Scope.Parameters.Count > 0 || context.Scope.RestrictToPath
            || !Uri.TryCreate(item.Target, UriKind.Absolute, out var uri) || uri.Fragment.Length > 0
            || !InstagramPublishers.TryReadProfile(uri.GetLeftPart(UriPartial.Path), out _, out string targetProfile)
            || targetProfile != profile)
            return new() { Outcome = FetchOutcome.NotApproved, Message = "The request must match the publisher profile saved in this pipeline." };
        int maximumPosts = InstagramCollectionLimits.PostsPerRun(context.Limits);
        if (maximumPosts is < 1 or > PipelineConstants.MaxItemsCap)
            return new() { Outcome = FetchOutcome.NotApproved, Message = "The saved Instagram pipeline has an invalid post limit." };
        string? after = null;
        int pageNumber = 0;
        int scanned = 0;
        int recognized = 0;
        if (item.Payload is { ValueKind: not JsonValueKind.Object })
            return new() { Outcome = FetchOutcome.UnexpectedContent, Message = "Invalid Instagram continuation state." };
        if (item.Payload is { ValueKind: JsonValueKind.Object } payload)
        {
            after = InstagramConfiguration.Text(payload, "after");
            if (!payload.TryGetProperty("page", out var number) || number.ValueKind != JsonValueKind.Number
                || !number.TryGetInt32(out pageNumber) || pageNumber < 1 || pageNumber >= InstagramCollectionLimits.MaximumPages
                || !payload.TryGetProperty("scanned", out var count) || count.ValueKind != JsonValueKind.Number
                || !count.TryGetInt32(out scanned) || scanned < 1 || scanned >= maximumPosts
                || !payload.TryGetProperty("recognized", out var accepted) || accepted.ValueKind != JsonValueKind.Number
                || !accepted.TryGetInt32(out recognized) || recognized < 0 || recognized > scanned || after is null)
                return new() { Outcome = FetchOutcome.UnexpectedContent, Message = "Invalid Instagram continuation state." };
        }
        int remaining = maximumPosts - scanned;
        int pageSize = Math.Min(InstagramCollectionLimits.RequestSize, remaining);
        InstagramMediaPage result;
        await using (var lease = await context.AcquireRequestAsync(cancellationToken))
        {
            if (lease.Outcome != FetchOutcome.Ok)
                return new() { Outcome = lease.Outcome, RetryAfter = lease.RetryAfter, Message = "The provider request gate did not admit this request." };
            result = await broker.ReadTracedAsync(username, pageSize, after, context, cancellationToken);
            await lease.CompleteAsync(result.Outcome, cancellationToken);
        }
        context.Observe(new() { Outcome = result.Outcome, Bytes = result.Usage.Bytes, RetryAfter = result.RetryAfter });
        if (result.Outcome != FetchOutcome.Ok)
            return new() { Outcome = result.Outcome, Usage = result.Usage, RetryAfter = result.RetryAfter, Message = result.Message };
        JsonElement[] media = [.. result.Media.Take(remaining)];
        scanned += media.Length;
        Capture[] captures = [.. media.Select(record => InstagramCaptureReader.Read(record, username, profile, context.Clock.GetUtcNow()))
            .OfType<Capture>().Select(capture => capture with
            {
                RequestUrl = result.RequestUrl ?? profile, FinalUrl = result.RequestUrl ?? profile, HttpStatus = result.HttpStatus,
            })];
        recognized += captures.Length;
        bool postLimit = result.Media.Count > remaining || result.After is not null && scanned >= maximumPosts;
        bool pageLimit = result.After is not null && pageNumber + 1 >= InstagramCollectionLimits.MaximumPages;
        string? limitMessage = postLimit ? "Configured post limit reached; older posts were not scanned."
            : pageLimit ? "Provider page safety limit reached before the post budget; older posts were not scanned." : null;
        StepTarget[] NextPage()
        {
            if (result.After is not { } cursor || media.Length == 0 || postLimit || pageLimit)
                return [];
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cursor)))[..24].ToLowerInvariant();
            return [new()
            {
                StepKey = StepKey, Target = profile + "?__harvest_cursor=" + digest,
                Payload = JsonSerializer.SerializeToElement(new { after = cursor, page = pageNumber + 1, scanned, recognized }),
            }];
        }
        var next = await context.RunOperationAsync("paginate", () => Task.FromResult(NextPage()),
            targets => new(Input: 1, Output: targets.Length, Message: limitMessage));
        return new()
        {
            Outcome = next.Length == 0 && captures.Length == 0 && (media.Length > 0 || scanned > 0 && recognized == 0)
                ? FetchOutcome.Unrecognized : FetchOutcome.Ok,
            Captures = captures, Next = next, Usage = result.Usage, EnumerationComplete = false,
            CompletionReason = postLimit ? RunReason.ItemLimit : pageLimit ? RunReason.PageLimit : null,
            Message = limitMessage ?? (captures.Length != result.Media.Count
                ? "Some malformed media records were skipped; review the source response." : null),
        };
    }
}
