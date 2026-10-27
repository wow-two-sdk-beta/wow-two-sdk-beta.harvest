using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;

/// <summary>Fetches one direct web step, selects a generic parser and reports refusal or link evidence unchanged.</summary>
public sealed class GenericStepBroker : IStepBroker
{
    private readonly IPageFetcher fetcher;
    private readonly IReadOnlyList<ICaptureReader> readers;

    /// <summary>Creates a broker for one adapter step.</summary>
    /// <param name="adapterKey">The generic family adapter's key.</param>
    /// <param name="stepKey">The enumerate, detail or check step.</param>
    /// <param name="fetcher">The direct, paced and policy-aware fetcher.</param>
    /// <param name="readers">The generic parsers, in precedence order.</param>
    public GenericStepBroker(string adapterKey, string stepKey, IPageFetcher fetcher, IEnumerable<ICaptureReader> readers)
    {
        AdapterKey = adapterKey;
        StepKey = stepKey;
        this.fetcher = fetcher;
        this.readers = readers.ToList();
    }

    /// <inheritdoc />
    public string AdapterKey { get; }

    /// <inheritdoc />
    public string StepKey { get; }

    /// <inheritdoc />
    public async Task<StepResult> ExecuteAsync(StepItem item, IStepContext context, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(item.Target, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return new() { Outcome = FetchOutcome.UnexpectedContent, Message = "The target is not an absolute HTTP URL." };
        IFetchDestinationPolicy? destinations = readers.OfType<IFetchDestinationPolicy>().FirstOrDefault();
        if (destinations is not null && !destinations.Allows(uri))
            return new() { Outcome = FetchOutcome.NotApproved, Message = "The target is outside the source's supported public routes." };
        var response = await fetcher.FetchAsync(new FetchRequest
        {
            Uri = uri, RobotsPolicy = context.Source.RobotsPolicy, MinDelay = context.Plan.Requirements.MinDelay, DestinationPolicy = destinations,
        }, cancellationToken);
        var document = response.Document;
        var bytes = response.Usage.Bytes;
        context.Observe(new StepSignal
        {
            Outcome = response.Outcome, HttpStatus = document?.StatusCode, Bytes = bytes,
            Challenge = response.Challenge, RetryAfter = response.RetryAfter,
        });
        var usage = response.Usage;
        var externalKey = item.Payload is { ValueKind: System.Text.Json.JsonValueKind.Object } payload
            && payload.TryGetProperty("externalKey", out var key) && key.ValueKind == System.Text.Json.JsonValueKind.String
            ? key.GetString()! : item.Target;
        ILinkAvailabilityReader? availability = document is null ? null : readers.OfType<ILinkAvailabilityReader>()
            .FirstOrDefault(candidate => candidate.CanCheck(document));
        if (response.Outcome == FetchOutcome.Gone && document?.StatusCode is 404 or 410 && availability is not null)
        {
            LinkObservation? missing = await availability.ReadAvailabilityAsync(document, externalKey, cancellationToken);
            return new()
            {
                Outcome = missing is null ? FetchOutcome.Unrecognized : FetchOutcome.Gone,
                Usage = usage, Links = missing is null ? [] : [missing],
                Message = missing is null ? "The missing response did not establish the requested source identity." : response.Message,
            };
        }
        if (response.Outcome != FetchOutcome.Ok)
            return new()
            {
                Outcome = response.Outcome, Challenge = response.Challenge, RetryAfter = response.RetryAfter,
                Message = response.Message, Usage = usage,
                Links = response.Outcome == FetchOutcome.Gone && document?.StatusCode is 404 or 410
                    ? [new() { ExternalKey = externalKey, Signal = LinkSignal.StrongNegative, Reason = $"direct_{document.StatusCode}", HttpStatus = document.StatusCode }] : [],
            };
        if (response.NotModified && (document is null || document.Body.IsEmpty))
            return new()
            {
                Outcome = FetchOutcome.Ok, Usage = usage,
                Links = StepKey == "check" ? [new() { ExternalKey = externalKey, Signal = LinkSignal.Present, Reason = "seen_again", HttpStatus = 304 }] : [],
            };
        if (document is null) return new() { Outcome = FetchOutcome.UnexpectedContent, Usage = usage };
        if (StepKey == "check" && availability is not null)
        {
            LinkObservation? observation = await availability.ReadAvailabilityAsync(document, externalKey, cancellationToken);
            return new()
            {
                Outcome = observation is null ? FetchOutcome.Unrecognized : FetchOutcome.Ok,
                Usage = usage, Links = observation is null ? [] : [observation],
                Message = observation is null ? "The source response did not establish the known link's availability." : null,
            };
        }
        if (StepKey == "check")
            return new()
            {
                Outcome = FetchOutcome.Ok, Usage = usage,
                Links = [new() { ExternalKey = externalKey, Signal = LinkSignal.Present, Reason = "seen_again", HttpStatus = document.StatusCode }],
            };
        var reader = readers.FirstOrDefault(candidate => candidate.CanRead(document));
        if (reader is null) return new() { Outcome = FetchOutcome.Unrecognized, Usage = usage, Message = "No generic reader understood the response." };
        var parsed = await reader.ReadAsync(document, cancellationToken);
        if (parsed is not Result<CaptureReadResult>.Success success) return new() { Outcome = FetchOutcome.UnexpectedContent, Usage = usage, Message = "The source document could not be parsed." };
        var read = success.Value;
        if (read.Captures.Any(capture => capture.Warnings.Contains("render_required")))
            return new() { Outcome = FetchOutcome.RenderRequired, Usage = usage, Message = "The page exposes only a JavaScript shell; no renderer is bound." };
        return new()
        {
            Outcome = FetchOutcome.Ok, Usage = usage, Captures = read.Captures,
            EnumerationComplete = StepKey == "enumerate" && read.Complete,
            Next = read.Discovered.Where(target => Uri.TryCreate(target.Url, UriKind.Absolute, out var found)
                    && found.IdnHost.Equals(uri.IdnHost, StringComparison.OrdinalIgnoreCase))
                .Select(target => new StepTarget { StepKey = target.IsListing ? "enumerate" : "detail", Target = target.Url }).ToList(),
            Links = read.Captures.Select(capture => new LinkObservation
            {
                ExternalKey = capture.ExternalKey, Signal = LinkSignal.Present, Reason = "seen_again", HttpStatus = document.StatusCode,
            }).ToList(),
        };
    }
}
