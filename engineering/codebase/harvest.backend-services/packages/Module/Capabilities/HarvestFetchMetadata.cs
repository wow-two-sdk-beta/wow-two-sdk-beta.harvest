using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Passes the typed fetch policy through the same handler used by raw step clients.</summary>
internal sealed record HarvestFetchMetadata(FetchOutcome Outcome, ChallengeKind? Challenge = null, TimeSpan? RetryAfter = null, string? Message = null)
{
    internal static readonly HttpRequestOptionsKey<FetchRequest> RequestKey = new("Harvest.FetchRequest");
    internal static readonly HttpRequestOptionsKey<HarvestFetchMetadata> ResponseKey = new("Harvest.FetchResponse");
}
