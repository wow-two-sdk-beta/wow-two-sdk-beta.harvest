using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Observes the numeric Meta application quota on the SDK client's existing HTTP transport.</summary>
internal sealed partial class InstagramQuotaHandler(ILogger<InstagramQuotaHandler> logger) : DelegatingHandler
{
    private const int MaximumHeaderLength = 2048;
    private static readonly AsyncLocal<QuotaSample?> Current = new();

    // The SDK returns its own diagnostic type. Keep the quota reading scoped to that single provider call.
    internal static async Task<(T Result, int? AppUsagePercent)> MeasureAsync<T>(Func<Task<T>> send)
    {
        QuotaSample? previous = Current.Value;
        var sample = new QuotaSample();
        Current.Value = sample;
        try
        {
            return (await send(), sample.HighestPercent);
        }
        finally
        {
            Current.Value = previous;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("X-App-Usage", out var values)
            && values.FirstOrDefault() is { Length: > 0 and <= MaximumHeaderLength } value)
            Observe(value, (int)response.StatusCode);
        return response;
    }

    private void Observe(string value, int status)
    {
        try
        {
            using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 3 });
            var usage = document.RootElement;
            if (usage.ValueKind != JsonValueKind.Object)
                return;
            int? calls = Percentage(usage, "call_count");
            int? cpu = Percentage(usage, "total_cputime");
            int? time = Percentage(usage, "total_time");
            if (calls is null && cpu is null && time is null)
                return;
            if (Current.Value is { } sample)
                sample.HighestPercent = Math.Max(sample.HighestPercent ?? 0, Math.Max(calls ?? 0, Math.Max(cpu ?? 0, time ?? 0)));
            Activity.Current?.SetTag("instagram.quota.call_count_pct", calls)
                .SetTag("instagram.quota.cpu_time_pct", cpu)
                .SetTag("instagram.quota.total_time_pct", time);
            LogAppUsage(logger, status, calls, cpu, time);
        }
        catch (JsonException)
        {
            // Quota telemetry is optional for a Graph response.
        }
    }

    private static int? Percentage(JsonElement usage, string key) =>
        usage.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int percent) && percent >= 0 ? percent : null;

    private sealed class QuotaSample
    {
        internal int? HighestPercent { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Information, Message =
        "Meta app quota usage (percent) HTTP {HttpStatus}, calls {CallCountPercent}, CPU {CpuTimePercent}, total time {TotalTimePercent}")]
    private static partial void LogAppUsage(ILogger logger, int httpStatus, int? callCountPercent, int? cpuTimePercent,
        int? totalTimePercent);
}
