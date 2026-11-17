using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Unit.Sources;

public sealed class SourceAddressTests
{
    private static readonly SourceModel Publisher = new()
    {
        Id = Guid.NewGuid(), ContainerId = Guid.NewGuid(), Family = SourceFamily.Instagram, Handle = "teplofest",
        Coverage = default, Completeness = default, Posture = default, Permission = default,
        AiAllowed = false, PoolShareable = false, RobotsPolicy = RobotsPolicy.Obey, ConsentBasis = default,
        Authority = 50, RetentionClass = "default", CapabilityPolicy = new(), State = default,
        StateSinceUtc = DateTimeOffset.UnixEpoch, CreatedAtUtc = DateTimeOffset.UnixEpoch,
    };
    private static readonly AdapterDescriptor Profile = new()
    {
        SourceUrl = "https://www.instagram.com/teplofest/", HostKey = "www.instagram.com", Steps = [],
        Plans = new Dictionary<WorkKind, IReadOnlyList<string>>(),
    };

    [Fact]
    public void Profile_identity_resolves_to_the_shared_origin_without_overwriting_identity()
    {
        var address = SourceAddress.Resolve(Publisher, Profile);

        address.Should().NotBeNull();
        address!.HostKey.Should().Be("www.instagram.com");
        SourceAddress.Resolve(Publisher, Profile with { HostKey = null }).Should().Be(address);
        Publisher.Handle.Should().Be("teplofest");
        RunPlanner.InScope("https://www.instagram.com/p/123/", Publisher, new(), Profile).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://evil.example/teplofest/")]
    [InlineData("http://www.instagram.com/teplofest/")]
    [InlineData("https://www.instagram.com:8443/teplofest/")]
    [InlineData("https://user@www.instagram.com/teplofest/")]
    public void Explicit_origin_rejects_host_scheme_port_and_credential_changes(string target)
    {
        RunPlanner.InScope(target, Publisher, new(), Profile).Should().BeFalse();
        RunPlanner.InScope(target, Publisher, new() { Url = target }, Profile).Should().BeFalse();
    }

    [Theory]
    [InlineData("/teplofest/", "www.instagram.com")]
    [InlineData("ftp://www.instagram.com/teplofest/", "www.instagram.com")]
    [InlineData("https://user@www.instagram.com/teplofest/", "www.instagram.com")]
    [InlineData("https://www.instagram.com/teplofest/?token=value", "www.instagram.com")]
    [InlineData("https://www.instagram.com/teplofest/#part", "www.instagram.com")]
    [InlineData("https://www.instagram.com/teplofest/", "teplofest")]
    [InlineData("https://www.instagram.com/teplofest/", "www.instagram.com:443")]
    [InlineData(null, "www.instagram.com")]
    public void Malformed_or_conflicting_descriptor_fails_closed(string? url, string host)
    {
        var descriptor = Profile with { SourceUrl = url, HostKey = host };
        SourceAddress.Resolve(Publisher, descriptor).Should().BeNull();
        RunPlanner.InScope(Profile.SourceUrl!, Publisher, new(), descriptor).Should().BeFalse();
    }

    [Fact]
    public void Legacy_host_sources_keep_existing_http_and_explicit_port_behavior()
    {
        var source = Publisher with { Family = SourceFamily.Site, Handle = "example.test" };
        SourceAddress.Resolve(source, null)!.HostKey.Should().Be("example.test");
        RunPlanner.InScope("http://example.test/path", source, new()).Should().BeTrue();
        RunPlanner.InScope("https://example.test:8443/path", source, new()).Should().BeFalse();
        RunPlanner.InScope("https://example.test:8443/path", source,
            new() { Url = "https://example.test:8443/start" }).Should().BeTrue();
        RunPlanner.InScope("https://evil.example/path", source, new()).Should().BeFalse();
    }

    [Fact]
    public void Path_restriction_remains_separate_from_the_declared_origin()
    {
        var scope = new PipelineScope { Url = Profile.SourceUrl, RestrictToPath = true };
        RunPlanner.InScope("https://www.instagram.com/teplofest/page2", Publisher, scope, Profile).Should().BeTrue();
        RunPlanner.InScope("https://www.instagram.com/teplostore_2022/", Publisher, scope, Profile).Should().BeFalse();
        RunPlanner.InScope(Profile.SourceUrl!, Publisher, new() { Url = "https://evil.example/" }, Profile).Should().BeFalse();
    }
}
