using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
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
            || context.Scope.Parameters.Count > 0 || context.Scope.RestrictToPath
            || !Uri.TryCreate(item.Target, UriKind.Absolute, out var uri) || uri.Fragment.Length > 0
            || !InstagramPublishers.TryReadProfile(uri.GetLeftPart(UriPartial.Path), out _, out string targetProfile)
            || targetProfile != profile)
            return new() { Outcome = FetchOutcome.NotApproved, Message = "The request must match the publisher profile saved in this pipeline." };
        int pageSize = context.Limits.PageSize ?? 25;
        int maximumPages = context.Limits.MaxPages ?? 4;
        if (pageSize is < 1 or > 50 || maximumPages is < 1 or > PipelineConstants.MaxPagesCap)
            return new() { Outcome = FetchOutcome.NotApproved, Message = "The saved Instagram pipeline has invalid page bounds." };
        string? after = null;
        int pageNumber = 0;
        if (item.Payload is { ValueKind: not JsonValueKind.Object })
            return new() { Outcome = FetchOutcome.UnexpectedContent, Message = "Invalid Instagram continuation state." };
        if (item.Payload is { ValueKind: JsonValueKind.Object } payload)
        {
            after = InstagramConfiguration.Text(payload, "after");
            if (!payload.TryGetProperty("page", out var number) || number.ValueKind != JsonValueKind.Number
                || !number.TryGetInt32(out pageNumber) || pageNumber < 1 || pageNumber >= maximumPages || after is null)
                return new() { Outcome = FetchOutcome.UnexpectedContent, Message = "Invalid Instagram continuation state." };
        }
        InstagramMediaPage result = await broker.ReadTracedAsync(username, pageSize, after, context, cancellationToken);
        context.Observe(new() { Outcome = result.Outcome, Bytes = result.Usage.Bytes, RetryAfter = result.RetryAfter });
        if (result.Outcome != FetchOutcome.Ok)
            return new() { Outcome = result.Outcome, Usage = result.Usage, RetryAfter = result.RetryAfter, Message = result.Message };
        Capture[] captures = [.. result.Media.Select(record => InstagramCaptureReader.Read(record, username, profile, context.Clock.GetUtcNow()))
            .OfType<Capture>().Select(capture => capture with
            {
                RequestUrl = result.RequestUrl ?? profile, FinalUrl = result.RequestUrl ?? profile, HttpStatus = result.HttpStatus,
            })];
        bool truncated = result.After is not null && pageNumber + 1 >= maximumPages;
        StepTarget[] NextPage()
        {
            if (result.After is not { } cursor || truncated)
                return [];
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cursor)))[..24].ToLowerInvariant();
            return [new()
            {
                StepKey = StepKey, Target = profile + "?__harvest_cursor=" + digest,
                Payload = JsonSerializer.SerializeToElement(new { after = cursor, page = pageNumber + 1 }),
            }];
        }
        var next = await context.RunOperationAsync("paginate", () => Task.FromResult(NextPage()),
            targets => new(Input: 1, Output: targets.Length,
                Message: truncated ? "The configured page limit was reached." : null));
        return new()
        {
            Outcome = captures.Length == 0 && result.Media.Count > 0 ? FetchOutcome.Unrecognized : FetchOutcome.Ok,
            Captures = captures, Next = next, Usage = result.Usage, EnumerationComplete = false,
            Message = truncated ? "Configured recent-media page limit reached; older posts were not scanned."
                : captures.Length != result.Media.Count ? "Some malformed media records were skipped; review the source response." : null,
        };
    }
}
