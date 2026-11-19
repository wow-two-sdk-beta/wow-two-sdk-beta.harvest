using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

namespace Harvest.Tests.Unit.Entities;

/// <summary>Cadence inference from stated starts, its confidence and the next predicted start.</summary>
public sealed class EntityCadenceTests
{
    private static readonly EntityCadenceOptions Defaults = new();

    [Theory]
    [InlineData("annual", "2024-10-21", "2025-10-20", "2026-10-19")]
    [InlineData("semiannual", "2025-04-10", "2025-10-12", "2026-04-09")]
    [InlineData("quarterly", "2026-01-15", "2026-04-14", "2026-07-16")]
    [InlineData("monthly", "2026-01-03", "2026-02-01", "2026-03-04", "2026-04-02")]
    [InlineData("irregular", "2025-01-10", "2025-03-01")]
    [InlineData("irregular", "2024-01-10", "2025-09-01")]
    public void Infer_ShouldNameThePeriodTheMedianGapFits(string expected, params string[] starts)
    {
        var estimate = EntityCadence.Infer(starts.Select(DateOnly.Parse), Defaults);

        estimate.Cadence.Should().Be(expected);
        estimate.Confidence.Should().BeInRange(0.01m, 1m);
    }

    [Fact]
    public void Infer_ShouldBeUnknown_BelowTwoDistinctStarts()
    {
        EntityCadence.Infer([], Defaults).Should().Be(new CadenceEstimate("unknown", 0m, null));
        EntityCadence.Infer([new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 19)], Defaults).Cadence.Should().Be("unknown");
    }

    [Fact]
    public void Infer_ShouldGrowMoreSure_WithMoreAndCloserEditions()
    {
        var two = EntityCadence.Infer([new DateOnly(2025, 10, 20), new DateOnly(2026, 10, 20)], Defaults);
        var three = EntityCadence.Infer([new DateOnly(2024, 10, 20), new DateOnly(2025, 10, 20), new DateOnly(2026, 10, 20)], Defaults);
        var loose = EntityCadence.Infer([new DateOnly(2025, 9, 1), new DateOnly(2026, 10, 10)], Defaults);

        two.Confidence.Should().Be(0.667m);
        three.Confidence.Should().BeGreaterThan(two.Confidence);
        loose.Cadence.Should().Be("annual");
        loose.Confidence.Should().BeLessThan(two.Confidence);
        EntityCadence.Predicts(two, Defaults).Should().BeTrue();
        EntityCadence.Predicts(loose, Defaults).Should().BeFalse("a gap near the tolerance's edge is not sure enough to predict");
    }

    [Fact]
    public void Infer_ShouldFollowTheConfiguredTolerance()
    {
        DateOnly[] starts = [new(2025, 9, 1), new(2026, 10, 10)];

        EntityCadence.Infer(starts, new EntityCadenceOptions { AnnualDays = 20 }).Cadence.Should().Be("irregular");
        EntityCadence.Infer(starts, new EntityCadenceOptions { AnnualDays = 120 }).Confidence
            .Should().BeGreaterThan(EntityCadence.Infer(starts, Defaults).Confidence);
        EntityCadence.Predicts(EntityCadence.Infer([new DateOnly(2025, 10, 20), new DateOnly(2026, 10, 20)], Defaults),
            new EntityCadenceOptions { PredictMinConfidence = 0.9 }).Should().BeFalse();
    }

    [Theory]
    [InlineData("annual", "2026-10-19", "2027-10-19")]
    [InlineData("annual", "2024-02-29", "2025-02-28")]
    [InlineData("semiannual", "2026-04-10", "2026-10-10")]
    [InlineData("quarterly", "2026-11-30", "2027-02-28")]
    [InlineData("monthly", "2026-01-31", "2026-02-28")]
    [InlineData("irregular", "2026-01-31", null)]
    [InlineData("unknown", "2026-01-31", null)]
    public void Next_ShouldAddThePeriodInCalendarUnits(string cadence, string last, string? expected)
    {
        EntityCadence.Next(DateOnly.Parse(last), cadence).Should().Be(expected is null ? null : DateOnly.Parse(expected));
    }
}
