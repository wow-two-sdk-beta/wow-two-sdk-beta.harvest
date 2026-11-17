using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

namespace HarvestAdapters.Tests.Unit.Instagram;

public sealed class InstagramSourceConfigurationTests
{
    [Theory]
    [InlineData("instagram.com")]
    [InlineData("www.instagram.com")]
    public void Connection_readiness_is_independent_of_publishers_and_does_not_expose_credentials(string host)
    {
        InstagramOptions settings = InstagramClientTests.Settings();
        var adapter = new InstagramSourceAdapter(Options.Create(settings));

        var configuration = adapter.DescribeConfiguration(Source(host));

        configuration.Readiness.Should().Be(SourceReadiness.Ready);
        adapter.Describe(Source(host)).Plans.Keys.Should().BeEquivalentTo(new[] { WorkKind.Capture, WorkKind.Sweep });
        ((object)adapter).Should().NotBeAssignableTo<ISourceSectionCatalog>();
        JsonSerializer.Serialize(configuration).Should().NotContain(settings.AccessToken).And.NotContain(settings.ProfessionalAccountId);
        adapter.ValidatePipeline(Source(host), new() { Url = $"https://{host}/new.publisher/" },
            new() { PageSize = 7, MaxPages = 3 }).IsSuccess.Should().BeTrue();
        adapter.ValidatePipeline(Source(host), new() { Url = $"https://{host}/another_publisher/" },
            new() { PageSize = 50, MaxPages = 100 }).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Missing_connection_does_not_prevent_saving_a_valid_profile_pipeline()
    {
        var adapter = new InstagramSourceAdapter();

        adapter.DescribeConfiguration(Source()).Readiness.Should().Be(SourceReadiness.ConfigurationRequired);
        adapter.ValidatePipeline(Source(), new() { Url = "https://www.instagram.com/publisher/" }, new())
            .IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://www.instagram.com/")]
    [InlineData("http://www.instagram.com/publisher/")]
    [InlineData("https://instagram.com/publisher/")]
    [InlineData("https://www.instagram.com:8443/publisher/")]
    [InlineData("https://user:secret@www.instagram.com/publisher/")]
    [InlineData("https://www.instagram.com/p/ABC/")]
    [InlineData("https://www.instagram.com/reels/")]
    [InlineData("https://www.instagram.com/direct/")]
    [InlineData("https://www.instagram.com/accounts/")]
    [InlineData("https://www.instagram.com/publisher/?access_token=synthetic")]
    [InlineData("https://www.instagram.com/publisher/#fragment")]
    [InlineData("https://www.instagram.com.example/publisher/")]
    public void Invalid_profile_scopes_are_rejected_before_a_pipeline_can_be_saved(string? url)
    {
        new InstagramSourceAdapter().ValidatePipeline(Source(), new() { Url = url }, new()).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Profile_pipeline_rejects_path_restriction_and_arbitrary_parameters()
    {
        var adapter = new InstagramSourceAdapter();
        var profile = new PipelineScope { Url = "https://www.instagram.com/publisher/" };

        adapter.ValidatePipeline(Source(), profile with { RestrictToPath = true }, new()).IsSuccess.Should().BeFalse();
        adapter.ValidatePipeline(Source(), profile with { Parameters = new Dictionary<string, string> { ["username"] = "other" } }, new())
            .IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(51, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Provider_page_and_run_limits_are_validated_on_the_saved_pipeline(int pageSize, int maxPages)
    {
        new InstagramSourceAdapter().ValidatePipeline(Source(), new() { Url = "https://www.instagram.com/publisher/" },
            new() { PageSize = pageSize, MaxPages = maxPages }).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData("www.instagram.com.example")]
    [InlineData("graph.facebook.com")]
    [InlineData("custom_publisher")]
    public void Unrelated_source_handles_are_not_served(string handle)
    {
        var adapter = new InstagramSourceAdapter();
        adapter.Serves(Source(handle)).Should().BeFalse();
        adapter.Serves(Source() with { Family = SourceFamily.Site }).Should().BeFalse();
    }

    private static SourceModel Source(string handle = "www.instagram.com") => new()
    {
        Id = Guid.NewGuid(), ContainerId = Guid.NewGuid(), Family = SourceFamily.Instagram, Handle = handle,
        Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window, Posture = SourcePosture.Official,
        Permission = SourcePermission.Open, AiAllowed = false, PoolShareable = false, RobotsPolicy = RobotsPolicy.Obey,
        ConsentBasis = ConsentBasis.None, Authority = 50, RetentionClass = "standard", CapabilityPolicy = new(),
        State = SourceState.Active, StateSinceUtc = DateTimeOffset.UnixEpoch, CreatedAtUtc = DateTimeOffset.UnixEpoch,
    };
}
