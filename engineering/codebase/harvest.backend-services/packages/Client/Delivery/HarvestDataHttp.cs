using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Registration;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Delivery;

/// <summary>Maps post and subscription HTTP responses onto the capability result contract.</summary>
internal static class HarvestDataHttp
{
    internal static async Task<Result<T>> SendAsync<T>(HttpClient http, HttpMethod method, string path, object? body, CancellationToken ct, bool unwrapped = false) where T : notnull
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptionsConstants.Default);
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                if (unwrapped)
                {
                    var value = await response.Content.ReadFromJsonAsync<T>(JsonOptionsConstants.Default, ct);
                    return value is null ? Result<T>.Fail(AppErrorFactory.SerializationFailed("The Harvest service answered without data.")) : Result<T>.Ok(value);
                }
                var envelope = await response.Content.ReadFromJsonAsync<DataEnvelope<T>>(JsonOptionsConstants.Default, ct);
                return envelope is { Data: { } data } ? Result<T>.Ok(data)
                    : Result<T>.Fail(AppErrorFactory.SerializationFailed("The Harvest service answered without data."));
            }
            ProblemBody? problem = null;
            try { problem = await response.Content.ReadFromJsonAsync<ProblemBody>(JsonOptionsConstants.Default, ct); }
            catch (JsonException) { }
            var message = problem?.Detail ?? problem?.Title ?? $"The Harvest service answered {(int)response.StatusCode}.";
            return Result<T>.Fail(response.StatusCode switch
            {
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => AppErrorFactory.Validation(message),
                HttpStatusCode.Unauthorized => AppErrorFactory.Unauthorized(message),
                HttpStatusCode.Forbidden => AppErrorFactory.Forbidden(message),
                HttpStatusCode.NotFound => AppErrorFactory.NotFound(message),
                HttpStatusCode.Conflict => AppErrorFactory.Conflict(message),
                _ => AppErrorFactory.ExternalUnavailable(message),
            });
        }
        catch (HttpRequestException) { return Result<T>.Fail(AppErrorFactory.ExternalUnavailable("The Harvest service is unreachable.")); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Result<T>.Fail(AppErrorFactory.ExternalUnavailable("The Harvest service timed out.")); }
        catch (JsonException exception) { return Result<T>.Fail(AppErrorFactory.SerializationFailed("The Harvest service answered malformed JSON.", exception)); }
    }
}
