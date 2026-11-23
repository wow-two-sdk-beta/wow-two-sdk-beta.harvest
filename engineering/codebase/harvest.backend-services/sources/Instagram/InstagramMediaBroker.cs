using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Integrations.Instagram;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Applies SCFD publisher policy and translates the reusable provider client into pipeline outcomes.</summary>
public sealed class InstagramMediaBroker(IInstagramClient client, IOptions<InstagramOptions> configuration) : IInstagramMediaBroker
{
    private const int QuotaPausePercent = 80;
    private static readonly TimeSpan QuotaPause = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RateLimitPause = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    public Task<InstagramMediaPage> ReadAsync(string username, int pageSize = 25, string? after = null, CancellationToken cancellationToken = default)
        => ReadAsync(username, pageSize, after, null, cancellationToken);

    /// <inheritdoc />
    public Task<InstagramMediaPage> ReadTracedAsync(string username, int pageSize, string? after, IStepContext context, CancellationToken cancellationToken)
        => ReadAsync(username, pageSize, after, context, cancellationToken);

    private async Task<InstagramMediaPage> ReadAsync(string username, int pageSize, string? after, IStepContext? context, CancellationToken cancellationToken)
    {
        Task<T> Trace<T>(string key, Func<Task<T>> execute, Func<T, StepOperationResult>? describe = null)
            => context?.RunOperationAsync(key, execute, describe) ?? execute();

        cancellationToken.ThrowIfCancellationRequested();
        InstagramOptions options = configuration.Value;
        InstagramMediaPage? rejected = await Trace("prepare-authentication", () => Task.FromResult(
            !InstagramConfiguration.IsValid(options)
                ? new InstagramMediaPage
                {
                    Outcome = FetchOutcome.CapabilityUnavailable,
                    Message = "Configure the HarvestInstagram endpoint, token, account ID, API version and request budget.",
                }
                : null), result => result is null ? new(Message: "Configured credentials are available.") : new("failed", Message: result.Message));
        if (rejected is not null)
            return rejected;
        if (InstagramPublishers.Normalize([username]).Count != 1 || pageSize is < 1 or > 50)
            return new() { Outcome = FetchOutcome.NotApproved, Message = "The saved Instagram pipeline needs a valid publisher and a page size from 1 to 50." };

        var (response, appUsage) = await Trace("fetch", () => InstagramQuotaHandler.MeasureAsync(() => client.GetBusinessDiscoveryMediaAsync(new()
        {
            ProfessionalAccountId = options.ProfessionalAccountId, ApiVersion = options.ApiVersion,
            Username = username, PageSize = pageSize, After = after,
        }, cancellationToken)), sample => sample.Result.Match(
            success => new StepOperationResult(Input: checked((int)success.Diagnostics.Requests), Output: success.Media.Count,
                HttpStatus: success.Diagnostics.HttpStatus, Message: sample.AppUsagePercent >= QuotaPausePercent
                    ? $"Meta app quota reached {sample.AppUsagePercent}%. Pausing queued requests for at least 5 minutes." : null),
            failure => new StepOperationResult("failed", Input: checked((int)failure.Diagnostics.Requests),
                HttpStatus: failure.Diagnostics.HttpStatus, Message: failure.Diagnostics.ProviderCode is int code
                    ? $"{failure.Message} Meta error code {code}." : failure.Message)));

        return await Trace("map-response", () => Task.FromResult(response.Match(
            success => FromDiagnostics(success.Diagnostics) with
            {
                Outcome = FetchOutcome.Ok, Media = success.Media, After = success.After,
                RetryAfter = appUsage >= QuotaPausePercent && success.Diagnostics.RetryAfter.GetValueOrDefault() < QuotaPause
                    ? QuotaPause : success.Diagnostics.RetryAfter,
            },
            failure => FromDiagnostics(failure.Diagnostics) with
            {
                Outcome = Map(failure.Kind), Message = failure.Message,
                RetryAfter = failure.Kind == InstagramFailureKind.RateLimited && failure.Diagnostics.RetryAfter.GetValueOrDefault() <= TimeSpan.Zero
                    ? RateLimitPause : failure.Diagnostics.RetryAfter,
            })),
            result => new(result.Outcome == FetchOutcome.Ok ? "completed" : "failed", Input: 1, Output: result.Media.Count, Message: result.Outcome.ToString()));
    }

    private static InstagramMediaPage FromDiagnostics(InstagramRequestDiagnostics diagnostics) => new()
    {
        Outcome = FetchOutcome.Ok, RequestUrl = diagnostics.RequestUrl, HttpStatus = diagnostics.HttpStatus,
        Usage = new() { Requests = diagnostics.Requests, Bytes = diagnostics.Bytes }, RetryAfter = diagnostics.RetryAfter,
    };

    private static FetchOutcome Map(InstagramFailureKind kind) => kind switch
    {
        InstagramFailureKind.CredentialsMissing => FetchOutcome.CapabilityUnavailable,
        InstagramFailureKind.LoginRequired => FetchOutcome.LoginRequired,
        InstagramFailureKind.Restricted => FetchOutcome.Restricted,
        InstagramFailureKind.RateLimited => FetchOutcome.RateLimited,
        InstagramFailureKind.InvalidRedirect => FetchOutcome.InvalidRedirect,
        InstagramFailureKind.ResponseTooLarge => FetchOutcome.ResponseTooLarge,
        InstagramFailureKind.InvalidRequest or InstagramFailureKind.UnexpectedContent => FetchOutcome.UnexpectedContent,
        InstagramFailureKind.HttpError => FetchOutcome.HttpError,
        InstagramFailureKind.ProviderFailed => FetchOutcome.ProviderFailed,
        InstagramFailureKind.NetworkError => FetchOutcome.NetworkError,
        InstagramFailureKind.Timeout => FetchOutcome.FetchTimeout,
        _ => FetchOutcome.ProviderFailed,
    };
}
