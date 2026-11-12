using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Captcha;

/// <summary>Implements 2Captcha's v2 submit/result protocol with one submission and bounded polling.</summary>
public sealed class TwoCaptchaSolver(
    IHttpClientFactory clients,
    IOptions<HarvestIntegrationOptions> integrations,
    TimeProvider clock) : ICaptchaSolver
{
    /// <summary>Holds the isolated guarded provider HTTP client name.</summary>
    public const string ProviderClientName = "Harvest.Integrations.Providers";

    /// <inheritdoc />
    public async Task<CaptchaTaskResult> SubmitAsync(CaptchaTask task, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TwoCaptchaOptions options = integrations.Value.Captcha;
        CaptchaTaskResult? unavailable = CheckConfiguration(options);
        if (unavailable is not null)
            return unavailable;
        if (task is null || !Enum.IsDefined(task.Kind) || !IntegrationValidation.IsWeb(task.WebsiteUrl)
            || task.WebsiteUrl.Scheme != "https" || string.IsNullOrWhiteSpace(task.SiteKey) || task.SiteKey.Length > 1024
            || !options.AllowedOrigins.Any(value => IntegrationValidation.TryOrigin(value, out Uri origin, true)
                && IntegrationValidation.SameOrigin(origin, task.WebsiteUrl)))
            return Failure("SOURCE_NOT_ALLOWED");

        var payload = new Dictionary<string, object?>
        {
            ["type"] = task.Kind == CaptchaKind.RecaptchaV2 ? "RecaptchaV2TaskProxyless" : "TurnstileTaskProxyless",
            ["websiteURL"] = task.WebsiteUrl.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped),
            ["websiteKey"] = task.SiteKey,
        };
        if (task.Kind == CaptchaKind.RecaptchaV2)
            payload["isInvisible"] = task.Invisible;
        else
        {
            if (task.Action is not null) payload["action"] = task.Action;
            if (task.Data is not null) payload["data"] = task.Data;
            if (task.PageData is not null) payload["pagedata"] = task.PageData;
        }
        return await CallAsync("createTask", new { clientKey = options.ApiKey, task = payload }, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CaptchaTaskResult> GetResultAsync(long taskId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TwoCaptchaOptions options = integrations.Value.Captcha;
        CaptchaTaskResult? unavailable = CheckConfiguration(options);
        if (unavailable is not null)
            return unavailable;
        if (taskId <= 0)
            return Failure("INVALID_TASK_ID");
        return await CallAsync("getTaskResult", new { clientKey = options.ApiKey, taskId }, taskId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CaptchaTaskResult> SolveAsync(CaptchaTask task, CancellationToken cancellationToken = default)
    {
        TwoCaptchaOptions options = integrations.Value.Captcha;
        CaptchaTaskResult submitted = await SubmitAsync(task, cancellationToken);
        if (submitted.Status != "submitted" || submitted.TaskId is not { } taskId)
            return submitted;
        using var timeout = new CancellationTokenSource(options.SolveTimeout, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            for (int index = 0; index < options.MaximumPolls; index++)
            {
                await Task.Delay(options.PollInterval, clock, deadline.Token);
                CaptchaTaskResult result = await GetResultAsync(taskId, deadline.Token);
                if (result.Status != "processing")
                    return result;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CaptchaTaskResult { TaskId = taskId, Status = "timeout", ErrorCode = "SOLVE_TIMEOUT" };
        }
        return new CaptchaTaskResult { TaskId = taskId, Status = "timeout", ErrorCode = "POLL_LIMIT" };
    }

    private CaptchaTaskResult? CheckConfiguration(TwoCaptchaOptions options)
    {
        if (!integrations.Value.Enabled)
            return new CaptchaTaskResult { Status = "disabled", ErrorCode = "CAPTCHA_DISABLED" };
        if (options is null)
            return new CaptchaTaskResult { Status = "unavailable", ErrorCode = "CAPTCHA_CONFIGURATION" };
        if (!options.Enabled)
            return new CaptchaTaskResult { Status = "disabled", ErrorCode = "CAPTCHA_DISABLED" };
        if (string.IsNullOrWhiteSpace(options.ApiKey) || !IntegrationValidation.TryProviderBase(options.BaseUrl, out _)
            || options.AllowedOrigins is null
            || options.PollInterval < TimeSpan.FromSeconds(5) || options.SolveTimeout <= options.PollInterval
            || options.SolveTimeout > TimeSpan.FromMinutes(10) || options.MaximumPolls is < 1 or > 120)
            return new CaptchaTaskResult { Status = "unavailable", ErrorCode = "CAPTCHA_CONFIGURATION" };
        return null;
    }

    private async Task<CaptchaTaskResult> CallAsync(string method, object body, long? taskId, CancellationToken ct)
    {
        IntegrationValidation.TryProviderBase(integrations.Value.Captcha.BaseUrl, out Uri root);
        using HttpClient http = clients.CreateClient(ProviderClientName);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, IntegrationValidation.RelativeEndpoint(root, method))
            {
                Content = JsonContent.Create(body),
                Version = System.Net.HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            };
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return Failure("PROVIDER_HTTP", taskId);
            using JsonDocument document = await ProviderJson.ReadAsync(response, ct);
            JsonElement value = document.RootElement;
            if (!value.TryGetProperty("errorId", out JsonElement error) || !error.TryGetInt32(out int errorId))
                return Failure("PROVIDER_PROTOCOL", taskId);
            if (errorId != 0)
            {
                string? code = IntegrationValidation.SafeCode(ReadString(value, "errorCode"));
                return Failure(code is not null && code.StartsWith("ERROR_", StringComparison.Ordinal)
                    && !code.Contains(integrations.Value.Captcha.ApiKey!, StringComparison.Ordinal)
                    ? code : "PROVIDER_ERROR", taskId);
            }
            if (taskId is null)
                return value.TryGetProperty("taskId", out JsonElement id) && id.TryGetInt64(out long submittedId) && submittedId > 0
                    ? new CaptchaTaskResult { TaskId = submittedId, Status = "submitted" }
                    : Failure("PROVIDER_PROTOCOL");
            string? status = value.TryGetProperty("status", out JsonElement state) && state.ValueKind == JsonValueKind.String ? state.GetString() : null;
            if (status == "processing")
                return new CaptchaTaskResult { TaskId = taskId, Status = "processing" };
            if (status != "ready" || !value.TryGetProperty("solution", out JsonElement solution)
                || solution.ValueKind != JsonValueKind.Object)
                return Failure("PROVIDER_PROTOCOL", taskId);
            string? token = ReadString(solution, "gRecaptchaResponse") ?? ReadString(solution, "token");
            if (string.IsNullOrWhiteSpace(token) || token.Length > 32_768)
                return Failure("PROVIDER_PROTOCOL", taskId);
            decimal? cost = value.TryGetProperty("cost", out JsonElement costValue)
                && decimal.TryParse(costValue.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedCost)
                && parsedCost >= 0 ? parsedCost : null;
            return new CaptchaTaskResult { TaskId = taskId, Status = "ready", Token = token, UserAgent = ReadString(solution, "userAgent"), CostUsd = cost };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure("PROVIDER_TIMEOUT", taskId);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException)
        {
            return Failure("PROVIDER_UNAVAILABLE", taskId);
        }
    }

    private static string? ReadString(JsonElement value, string key)
    {
        return value.TryGetProperty(key, out JsonElement item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    }

    private static CaptchaTaskResult Failure(string code, long? taskId = null)
    {
        return new CaptchaTaskResult { TaskId = taskId, Status = "failed", ErrorCode = code };
    }
}
