using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Declares bounded recent-media collection for saved professional publisher profile URLs.</summary>
public sealed class InstagramSourceAdapter(IOptions<InstagramOptions>? options = null) : ISourceAdapter, ISourceConfigurationProvider
{
    /// <inheritdoc />
    public SourceConfigurationModel DescribeConfiguration(SourceModel source)
    {
        InstagramOptions settings = options?.Value ?? new();
        bool configured = InstagramBusinessDiscoveryClient.HasValidConfiguration(settings)
            && settings.AllowedUsernames is { Length: > 0 }
            && settings.AllowedUsernames.All(name => name is not null && InstagramBusinessDiscoveryClient.Username().IsMatch(name));
        return new()
        {
            DisplayName = "Instagram professional publishers",
            Readiness = configured ? SourceReadiness.Ready : SourceReadiness.ConfigurationRequired,
            Message = configured ? "Local API configuration is present; publisher access is checked during execution."
                : "An operator must enable the API connection, credentials, API version and publisher allowlist.",
            ScopeSchema = SourceScopeSchemas.WebUrl("Save the profile URL of a publisher approved by this runtime."),
        };
    }

    /// <summary>The stable private adapter key.</summary>
    public const string AdapterKey = "instagram-business-discovery";
    /// <inheritdoc />
    public string Key => AdapterKey;
    /// <inheritdoc />
    public SourceFamily Family => SourceFamily.Instagram;
    /// <inheritdoc />
    public bool Serves(SourceModel source) => source.Family == Family && source.Handle is "www.instagram.com" or "instagram.com";
    /// <inheritdoc />
    public AdapterDescriptor Describe(SourceModel source) => new()
    {
        Steps = [new() { Key = "enumerate", Role = StepRole.Enumerate, Requires = new() { MinDelay = TimeSpan.FromSeconds(5) } }],
        Plans = new Dictionary<WorkKind, IReadOnlyList<string>>
        {
            [WorkKind.Capture] = ["enumerate"], [WorkKind.Discover] = ["enumerate"], [WorkKind.Sweep] = ["enumerate"],
        },
    };
}
