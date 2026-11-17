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
    public void Collection_metadata_exposes_only_posts_and_preserves_legacy_effective_bounds()
    {
        var adapter = new InstagramSourceAdapter();
        var limits = adapter.DescribeConfiguration(Source()).ScopeSchema.GetProperty("limits");
        limits.EnumerateObject().Select(property => property.Name).Should().Equal("maxItems");
        limits.GetProperty("maxItems").GetProperty("title").GetString().Should().Be("Posts per run");
        adapter.ResolveRunLimits(Source(), new(), WorkKind.Capture).MaxItems.Should().Be(100);
        adapter.ResolveRunLimits(Source(), new() { PageSize = 1, MaxPages = 1 }, WorkKind.Capture).MaxItems.Should().Be(1);
        var resolved = adapter.ResolveRunLimits(Source(), new() { MaxItems = 150, PageSize = 1, MaxPages = 1 }, WorkKind.Capture);
        resolved.MaxItems.Should().Be(150);
        resolved.PageSize.Should().Be(50);
        resolved.MaxPages.Should().Be(100);
        var processing = new PipelineLimits { MaxItems = 7 };
        adapter.ResolveRunLimits(Source(), processing, WorkKind.Process).Should().Be(processing);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2001)]
    public void Invalid_post_limits_are_rejected(int maximum)
    {
        new InstagramSourceAdapter().ValidatePipeline(Source(), new() { Url = "https://www.instagram.com/publisher/" },
            new() { MaxItems = maximum }).IsSuccess.Should().BeFalse();
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
    [InlineData("teplofest")]
    [InlineData("teplostore_2022")]
    public void Publisher_sources_expose_one_bound_profile_and_share_the_provider_host(string username)
    {
        var adapter = new InstagramSourceAdapter();
        var source = Source(username);
        string profile = $"https://www.instagram.com/{username}/";

        adapter.Serves(source).Should().BeTrue();
        adapter.Describe(source).SourceUrl.Should().Be(profile);
        adapter.Describe(source).HostKey.Should().Be("www.instagram.com");
        adapter.DescribeConfiguration(source).ScopeSchema.GetProperty("properties").GetProperty("url").GetProperty("const")
            .GetString().Should().Be(profile);
        adapter.ValidatePipeline(source, new() { Url = profile }, new()).IsSuccess.Should().BeTrue();
        adapter.ValidatePipeline(source, new() { Url = "https://www.instagram.com/other.publisher/" }, new())
            .IsSuccess.Should().BeFalse();
        adapter.ValidatePipeline(source, new() { Url = $"https://instagram.com/{username}/" }, new())
            .IsSuccess.Should().BeFalse();
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
    [InlineData("bad/handle")]
    [InlineData("publisher with whitespace")]
    [InlineData("https://www.instagram.com/publisher/")]
    [InlineData("@publisher")]
    [InlineData("accounts")]
    public void Invalid_publisher_source_handles_are_not_served(string handle)
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
