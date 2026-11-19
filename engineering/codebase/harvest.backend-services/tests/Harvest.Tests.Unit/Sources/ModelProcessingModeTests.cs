using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace Harvest.Tests.Unit.Sources;

/// <summary>The per-source model-processing mode beside the older <c>aiAllowed</c> switch: stored runs and older callers
/// that send only the switch map to <c>auto</c> or <c>off</c>, a mode wins whatever the property order, and an update
/// whose two switches disagree is a conflict.</summary>
public sealed class ModelProcessingModeTests
{
    [Theory]
    [InlineData("""{"aiAllowed":true}""", ModelProcessingMode.Auto)]
    [InlineData("""{"aiAllowed":false}""", ModelProcessingMode.Off)]
    [InlineData("""{"aiAllowed":true,"modelProcessing":"manual"}""", ModelProcessingMode.Manual)]
    [InlineData("""{"modelProcessing":"manual","aiAllowed":true}""", ModelProcessingMode.Manual)]
    [InlineData("""{"modelProcessing":"off","aiAllowed":false}""", ModelProcessingMode.Off)]
    [InlineData("{}", ModelProcessingMode.Off)]
    public void Source_ShouldReadItsModeFromEitherSwitch_InAnyOrder(string json, ModelProcessingMode expected)
    {
        var stored = JsonSerializer.SerializeToNode(Manual(), JsonOptionsConstants.Default)!.AsObject();
        stored.Remove("aiAllowed");
        stored.Remove("modelProcessing");
        foreach (var (name, value) in JsonNode.Parse(json)!.AsObject())
            stored[name] = value?.DeepClone();
        var source = stored.Deserialize<SourceModel>(JsonOptionsConstants.Default)!;

        source.ModelProcessing.Should().Be(expected);
        source.AiAllowed.Should().Be(expected != ModelProcessingMode.Off);
    }

    [Fact]
    public void Source_ShouldWriteBothSwitches_SoOlderReadersKeepTheirs()
    {
        var json = JsonSerializer.Serialize(Manual(), JsonOptionsConstants.Default);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("aiAllowed").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("modelProcessing").GetString().Should().Be("manual");
    }

    private static SourceModel Manual() => new()
    {
        Id = Guid.Empty, ContainerId = Guid.Empty, Family = SourceFamily.Site, Handle = "a.example", Coverage = SourceCoverage.Whole,
        Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch, Permission = SourcePermission.Open,
        ModelProcessing = ModelProcessingMode.Manual, PoolShareable = false, RobotsPolicy = RobotsPolicy.Obey, ConsentBasis = ConsentBasis.None,
        Authority = 50, RetentionClass = "standard", CapabilityPolicy = new CapabilityPolicy(), State = SourceState.Active,
        StateSinceUtc = DateTimeOffset.UnixEpoch, CreatedAtUtc = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void Registration_ShouldStartAutomatic_AndMapTheOlderSwitch()
    {
        new SourceRegistration { ContainerId = Guid.Empty, Family = SourceFamily.Site, Handle = "a", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch }.ModelProcessing.Should().Be(ModelProcessingMode.Auto);
        new SourceRegistration { ContainerId = Guid.Empty, Family = SourceFamily.Site, Handle = "a", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch, AiAllowed = false }.ModelProcessing.Should().Be(ModelProcessingMode.Off);
    }

    [Theory]
    [InlineData(null, null, null, false)]
    [InlineData(null, true, ModelProcessingMode.Auto, false)]
    [InlineData(null, false, ModelProcessingMode.Off, false)]
    [InlineData(ModelProcessingMode.Manual, null, ModelProcessingMode.Manual, false)]
    [InlineData(ModelProcessingMode.Manual, true, ModelProcessingMode.Manual, false)]
    [InlineData(ModelProcessingMode.Manual, false, ModelProcessingMode.Manual, true)]
    [InlineData(ModelProcessingMode.Off, true, ModelProcessingMode.Off, true)]
    public void Update_ShouldResolveTheMode_AndFlagSwitchesThatDisagree(ModelProcessingMode? mode, bool? aiAllowed, ModelProcessingMode? expected,
        bool conflict) =>
        new SourceUpdate { ModelProcessing = mode, AiAllowed = aiAllowed }.ResolveModelProcessing().Should().Be((expected, conflict));
}
