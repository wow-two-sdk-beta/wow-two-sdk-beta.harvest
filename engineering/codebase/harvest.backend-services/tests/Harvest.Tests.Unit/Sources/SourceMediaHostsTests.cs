using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Unit.Sources;

/// <summary>The image hosts an adapter may declare: none by default, and a declared set copies a post's whole image list
/// at the two second gap unless the adapter narrows it.</summary>
public sealed class SourceMediaHostsTests
{
    [Fact]
    public void Descriptor_ShouldDeclareNoMediaHosts_UnlessTheAdapterDoes()
    {
        new AdapterDescriptor { Steps = [], Plans = new Dictionary<WorkKind, IReadOnlyList<string>>() }.Media.Should().BeNull();
    }

    [Fact]
    public void Hosts_ShouldDefaultToTheWholeImageListAtATwoSecondGap()
    {
        var media = new SourceMediaHosts { Domains = ["olxcdn.com"] };

        media.Limit.Should().Be(SourceConstants.MaxPostImages);
        media.MinDelay.Should().Be(TimeSpan.FromSeconds(2));
    }
}
