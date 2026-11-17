using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Represents one listing seen in a section's catalog.</summary>
/// <param name="Id">The discovery's internal identity.</param>
/// <param name="ExternalKey">The source's listing identity.</param>
/// <param name="Url">The original listing URL.</param>
/// <param name="Title">The catalog title, when supplied.</param>
/// <param name="PublishedAtUtc">The source publication time, when supplied.</param>
/// <param name="LastSeenAtUtc">When this section last saw the listing.</param>
/// <param name="CatalogUrl">The page that found the listing.</param>
/// <param name="State">The detail capture state.</param>
/// <param name="NextDetailAtUtc">The earliest time another detail attempt may start.</param>
/// <param name="PostId">The captured post's identity, if available.</param>
/// <param name="NativeFields">The source's catalog metadata.</param>
public sealed record PipelineDiscoveryModel(Guid Id, string ExternalKey, string Url, string? Title,
    DateTimeOffset? PublishedAtUtc, DateTimeOffset LastSeenAtUtc, string CatalogUrl, string State,
    DateTimeOffset NextDetailAtUtc, Guid? PostId, JsonElement? NativeFields);
