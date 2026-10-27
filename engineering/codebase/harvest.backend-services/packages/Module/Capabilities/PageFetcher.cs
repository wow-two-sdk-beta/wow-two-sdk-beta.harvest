using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Turns guarded HTTP responses into the engine's fetch outcomes.</summary>
internal sealed class PageFetcher(IFetchClientFactory factory, TimeProvider clock) : IPageFetcher
{
    public async Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Uri);
        using var client = factory.CreateClient(request.Cookies);
        using var message = new HttpRequestMessage(HttpMethod.Get, request.Uri);
        message.Options.Set(HarvestFetchMetadata.RequestKey, request);
        try
        {
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            message.Options.TryGetValue(HarvestFetchMetadata.ResponseKey, out var metadata);
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return new FetchResponse
            {
                Outcome = metadata?.Outcome ?? (response.IsSuccessStatusCode ? FetchOutcome.Ok : FetchOutcome.HttpError),
                Challenge = metadata?.Challenge,
                RetryAfter = metadata?.RetryAfter,
                Message = metadata?.Message,
                NotModified = response.StatusCode == System.Net.HttpStatusCode.NotModified,
                Usage = FetchUsageCounters.Read(message),
                Document = new FetchedDocument
                {
                    RequestUrl = request.Uri,
                    FinalUrl = response.RequestMessage?.RequestUri ?? request.Uri,
                    StatusCode = (int)response.StatusCode,
                    ContentType = response.Content.Headers.ContentType?.MediaType,
                    Charset = response.Content.Headers.ContentType?.CharSet,
                    Headers = response.Headers.Concat(response.Content.Headers)
                        .SelectMany(header => header.Value.Select(value => new KeyValuePair<string, string>(header.Key, value))).ToArray(),
                    Body = body,
                    FetchedAtUtc = clock.GetUtcNow()
                }
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FetchResponse { Outcome = FetchOutcome.FetchTimeout, Message = "Direct fetch deadline expired.", Usage = FetchUsageCounters.Read(message) };
        }
        catch (HttpRequestException)
        {
            return new FetchResponse { Outcome = FetchOutcome.NetworkError, Message = "Direct connection failed.", Usage = FetchUsageCounters.Read(message) };
        }
    }
}
