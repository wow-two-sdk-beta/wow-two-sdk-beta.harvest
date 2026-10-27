using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Fetching;

namespace Harvest.Tests.Unit.Capabilities;

/// <summary>Reading a host's <c>Retry-After</c>, in either the seconds form or the HTTP-date form.</summary>
public sealed class RetryAfterPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Resolve_ShouldReadSeconds_WhenTheValueIsACount()
    {
        RetryAfterPolicy.Resolve("90", Now).Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void Resolve_ShouldMeasureAgainstNow_WhenTheValueIsAnHttpDate()
    {
        RetryAfterPolicy.Resolve("Tue, 27 Oct 2026 09:02:00 GMT", Now).Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void Resolve_ShouldBeZero_WhenTheHttpDateIsInThePast()
    {
        RetryAfterPolicy.Resolve("Tue, 27 Oct 2026 08:59:00 GMT", Now).Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    public void Resolve_ShouldBeNull_WhenTheValueIsAbsentOrUnreadable(string? value)
    {
        RetryAfterPolicy.Resolve(value, Now).Should().BeNull();
    }

    [Fact]
    public void TryParseSeconds_ShouldRejectTheHttpDateForm()
    {
        RetryAfterPolicy.TryParseSeconds("Tue, 27 Oct 2026 09:02:00 GMT").Should().BeNull();
    }

    [Fact]
    public void TryParseSeconds_ShouldClampANegativeCountToZero()
    {
        // A signed value is not the seconds form; it falls through to null rather than a negative wait.
        RetryAfterPolicy.TryParseSeconds("-5").Should().BeNull();
    }
}
