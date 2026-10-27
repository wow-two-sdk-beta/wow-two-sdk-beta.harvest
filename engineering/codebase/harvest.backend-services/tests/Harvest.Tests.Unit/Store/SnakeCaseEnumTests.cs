using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace Harvest.Tests.Unit.Store;

/// <summary>Enums travel to the store as the snake_case text the schema's CHECK constraints name.</summary>
public sealed class SnakeCaseEnumTests
{
    [Theory]
    [InlineData(SourcePosture.LicensedProvider, "licensed_provider")]
    [InlineData(SourceState.PreviewUnavailable, "preview_unavailable")]
    [InlineData(SourceFamily.Whatsapp, "whatsapp")]
    [InlineData(SourceFamily.X, "x")]
    public void ToText_ShouldWriteSnakeCase_WhenTheNameHasSeveralWords(Enum value, string expected)
    {
        var text = value switch
        {
            SourcePosture posture => SnakeCaseEnum<SourcePosture>.ToText(posture),
            SourceState state => SnakeCaseEnum<SourceState>.ToText(state),
            SourceFamily family => SnakeCaseEnum<SourceFamily>.ToText(family),
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

        text.Should().Be(expected);
    }

    [Fact]
    public void Parse_ShouldRoundTripEveryValue_WhenTheTextCameFromToText()
    {
        foreach (var state in Enum.GetValues<PipelineState>())
            SnakeCaseEnum<PipelineState>.Parse(SnakeCaseEnum<PipelineState>.ToText(state)).Should().Be(state);
    }

    [Fact]
    public void Parse_ShouldThrow_WhenTheTextIsUnknown()
    {
        var parse = () => SnakeCaseEnum<PipelineState>.Parse("PendingApproval");

        parse.Should().Throw<InvalidOperationException>();
    }
}
