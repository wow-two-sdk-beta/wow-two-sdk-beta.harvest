using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace Harvest.Tests.Unit.Engine;

/// <summary>Checks schedule slots against explicit clocks, zones and windows.</summary>
public sealed class ScheduleCalculatorTests
{
    [Fact]
    public void Interval_ShouldStartNowThenAdvanceByItsCadence()
    {
        var schedule = Schedule(ScheduleMode.Interval) with { Every = TimeSpan.FromMinutes(20) };
        var now = DateTimeOffset.Parse("2026-10-27T10:03:00Z");
        ScheduleCalculator.Next(schedule, now, first: true).Should().Be(now);
        ScheduleCalculator.Next(schedule, now).Should().Be(now.AddMinutes(20));
    }

    [Fact]
    public void Cron_ShouldReadWallTimeInTheDeclaredZone()
    {
        var schedule = Schedule(ScheduleMode.Cron) with { Cron = "0 9 * * *", TimeZone = "Asia/Tashkent" };
        ScheduleCalculator.Next(schedule, DateTimeOffset.Parse("2026-10-27T03:30:00Z"))
            .Should().Be(DateTimeOffset.Parse("2026-10-27T04:00:00Z"));
    }

    [Fact]
    public void Cron_ShouldSupportSixFieldExpressions()
    {
        var schedule = Schedule(ScheduleMode.Cron) with { Cron = "*/10 * * * * *" };
        ScheduleCalculator.Next(schedule, DateTimeOffset.Parse("2026-10-27T10:00:05Z"))
            .Should().Be(DateTimeOffset.Parse("2026-10-27T10:00:10Z"));
    }

    [Fact]
    public void Window_ShouldDeferAnEveningSlotUntilNextLocalMorning()
    {
        var schedule = Schedule(ScheduleMode.Interval) with
        {
            Every = TimeSpan.FromMinutes(20), TimeZone = "Asia/Tashkent", Window = new() { Start = new(9, 0), End = new(18, 0) },
        };
        ScheduleCalculator.Next(schedule, DateTimeOffset.Parse("2026-10-27T14:00:00Z"), first: true)
            .Should().Be(DateTimeOffset.Parse("2026-10-28T04:00:00Z"));
    }

    [Fact]
    public void OvernightWindow_ShouldAllowLateNightAndDeferMidday()
    {
        var schedule = Schedule(ScheduleMode.Interval) with
        {
            Every = TimeSpan.FromMinutes(20), TimeZone = "Asia/Tashkent", Window = new() { Start = new(22, 0), End = new(6, 0) },
        };
        var night = DateTimeOffset.Parse("2026-10-27T18:00:00Z");
        ScheduleCalculator.Next(schedule, night, first: true).Should().Be(night);
        ScheduleCalculator.Next(schedule, DateTimeOffset.Parse("2026-10-27T07:00:00Z"), first: true)
            .Should().Be(DateTimeOffset.Parse("2026-10-27T17:00:00Z"));
    }

    [Fact]
    public void Window_ShouldAdvanceAcrossASpringDstGap()
    {
        var schedule = Schedule(ScheduleMode.Interval) with
        {
            Every = TimeSpan.FromMinutes(20), TimeZone = "America/New_York", Window = new() { Start = new(2, 30), End = new(4, 0) },
        };
        ScheduleCalculator.Next(schedule, DateTimeOffset.Parse("2026-03-08T05:00:00Z"), first: true)
            .Should().Be(DateTimeOffset.Parse("2026-03-08T07:00:00Z"));
    }

    [Fact]
    public void DisabledOrManualSchedule_ShouldNeverQueue()
    {
        var now = DateTimeOffset.UtcNow;
        ScheduleCalculator.Next(Schedule(ScheduleMode.Manual), now).Should().BeNull();
        ScheduleCalculator.Next(Schedule(ScheduleMode.Interval) with { Enabled = false }, now).Should().BeNull();
    }

    private static ScheduleModel Schedule(ScheduleMode mode) => new()
    { Id = Guid.NewGuid(), Kind = WorkKind.Capture, Mode = mode, TimeZone = "UTC", Enabled = true };
}
