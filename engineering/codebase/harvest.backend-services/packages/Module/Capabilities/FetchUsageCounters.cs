using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Accumulates one policy fetch's actual transport work, including robots and redirect hops.</summary>
internal sealed class FetchUsageCounters
{
    internal static readonly HttpRequestOptionsKey<FetchUsageCounters> Key = new("Harvest.FetchUsage");
    internal long Requests { get; set; }
    internal long Bytes { get; set; }
    internal StepUsage Snapshot() => new() { Requests = Requests, Bytes = Bytes };

    internal static StepUsage Read(HttpRequestMessage request) => request.Options.TryGetValue(Key, out var usage) ? usage.Snapshot() : new();
}
