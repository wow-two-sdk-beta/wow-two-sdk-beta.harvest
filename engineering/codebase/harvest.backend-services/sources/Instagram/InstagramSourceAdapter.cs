using Microsoft.Extensions.Options;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Declares bounded recent-media collection for saved professional publisher profile URLs.</summary>
public sealed class InstagramSourceAdapter(IOptions<InstagramOptions>? options = null) : ISourceAdapter, ISourceConfigurationProvider, ISourcePipelineValidator, ISourceRunLimitsResolver
{
    /// <inheritdoc />
    public SourceConfigurationModel DescribeConfiguration(SourceModel source)
    {
        InstagramOptions settings = options?.Value ?? new();
        bool configured = Serves(source) && InstagramConfiguration.IsValid(settings);
        string? sourceProfile = InstagramPublishers.TryReadSourceProfile(source.Handle, out _, out string profile) ? profile : null;
        return new()
        {
            DisplayName = "Instagram professional publishers",
            Readiness = configured ? SourceReadiness.Ready : SourceReadiness.ConfigurationRequired,
            Message = configured ? "Local API configuration is present; publisher access is checked during execution."
                : "Configure the Meta token, professional account ID, API version and a request budget from 1 to 720 per hour.",
            ScopeSchema = JsonSerializer.SerializeToElement(new
            {
                schema = "harvest.scope.v1", type = "object",
                properties = new { url = new { type = "string", format = "uri", title = "Publisher profile URL", @const = sourceProfile,
                    description = "Save the public Instagram profile URL of an approved professional publisher." } },
                required = new[] { "url" }, additionalProperties = false,
                limits = new
                {
                    maxItems = new
                    {
                        minimum = 1, maximum = PipelineConstants.MaxItemsCap, @default = InstagramCollectionLimits.DefaultPosts,
                        title = "Posts per run", description = "Checks up to this many recent posts. Existing posts count.",
                        legacyProduct = new[] { new { key = "pageSize", @default = 25 }, new { key = "maxPages", @default = 4 } },
                    },
                },
            }),
        };
    }

    /// <inheritdoc />
    public Result ValidatePipeline(SourceModel source, PipelineScope scope, PipelineLimits limits)
    {
        if (!Serves(source) || !InstagramPublishers.TryReadProfile(scope.Url, out _, out var profile)
            || !InstagramPublishers.MatchesSource(source.Handle, profile) || scope.RestrictToPath || scope.Parameters.Count > 0)
            return Result.Fail(RegistrationErrors.Invalid("Instagram pipelines require the source's public publisher profile URL, without query overrides or path restriction."));
        if (limits.PageSize is < 1 or > 50 || limits.MaxPages is < 1 or > PipelineConstants.MaxPagesCap)
            return Result.Fail(RegistrationErrors.Invalid("Instagram page size must be 1–50 and the run page limit 1–100."));
        if (limits.MaxItems is < 1 or > PipelineConstants.MaxItemsCap)
            return Result.Fail(RegistrationErrors.Invalid("Instagram posts per run must be 1–2000."));
        return Result.Ok();
    }

    /// <inheritdoc />
    public PipelineLimits ResolveRunLimits(SourceModel source, PipelineLimits limits, WorkKind kind) =>
        kind is WorkKind.Capture or WorkKind.Sweep ? limits with
        {
            MaxItems = InstagramCollectionLimits.PostsPerRun(limits),
            PageSize = InstagramCollectionLimits.RequestSize, MaxPages = InstagramCollectionLimits.MaximumPages,
        } : limits;

    /// <summary>The stable private adapter key.</summary>
    public const string AdapterKey = "instagram-business-discovery";
    /// <inheritdoc />
    public string Key => AdapterKey;
    /// <inheritdoc />
    public SourceFamily Family => SourceFamily.Instagram;
    /// <inheritdoc />
    public bool Serves(SourceModel source) => source.Family == Family
        && (InstagramPublishers.IsLegacyDomain(source.Handle) || InstagramPublishers.TryReadSourceProfile(source.Handle, out _, out _));
    /// <inheritdoc />
    public AdapterDescriptor Describe(SourceModel source) => new()
    {
        SourceUrl = InstagramPublishers.TryReadSourceProfile(source.Handle, out _, out string profile) ? profile : null,
        HostKey = InstagramPublishers.IsLegacyDomain(source.Handle) ? null : "www.instagram.com",
        Steps = [new()
        {
            Key = "enumerate", Role = StepRole.Enumerate,
            Requires = new() { QueueAtHostPace = true },
        }],
        Plans = new Dictionary<WorkKind, IReadOnlyList<string>>
        {
            [WorkKind.Capture] = ["enumerate"], [WorkKind.Sweep] = ["enumerate"],
        },
    };
}
