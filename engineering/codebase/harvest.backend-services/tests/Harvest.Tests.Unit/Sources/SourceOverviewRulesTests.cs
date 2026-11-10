using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Unit.Sources;

/// <summary>The overview's pure rules: the state order, the 14-day series, a section's harvesting switch, the host's
/// health and the limits an update merges.</summary>
public sealed class SourceOverviewRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 15, 30, 0, TimeSpan.Zero);

    private static readonly SourceHealthModel Calm = new() { HostKey = "www.afisha.uz", MinDelayMs = 5000, ConsecutiveFailures = 0 };

    [Fact]
    public void State_ShouldBeHarvesting_WhenARunIsActiveWhateverElseHolds()
    {
        var state = SourceOverviewRules.State(true, Calm with { PausedUntil = Now.AddHours(1), ConsecutiveFailures = 9 },
            Harvest(RunStatus.Failed), []);

        state.Should().Be(SourceHarvestState.Harvesting);
    }

    [Theory]
    [InlineData(true, 0, RunStatus.Completed)]
    [InlineData(false, 3, RunStatus.Completed)]
    [InlineData(false, 0, RunStatus.Failed)]
    public void State_ShouldNeedAttention_WhenTheHostIsPausedFailingOrTheLastHarvestFailed(bool paused, int failures, RunStatus last)
    {
        var health = Calm with { PausedUntil = paused ? Now.AddMinutes(5) : null, ConsecutiveFailures = failures };

        SourceOverviewRules.State(false, health, Harvest(last), [Section(harvesting: true)]).Should().Be(SourceHarvestState.NeedsAttention);
    }

    [Fact]
    public void State_ShouldBeHealthyOrIdle_ByWhetherASectionHarvests()
    {
        var partial = Harvest(RunStatus.Partial);

        SourceOverviewRules.State(false, Calm with { ConsecutiveFailures = 2 }, partial, [Section(false), Section(true)])
            .Should().Be(SourceHarvestState.Healthy);
        SourceOverviewRules.State(false, Calm, partial, [Section(false)]).Should().Be(SourceHarvestState.Idle);
        SourceOverviewRules.State(false, Calm, null, []).Should().Be(SourceHarvestState.Idle);
    }

    [Fact]
    public void DailySeries_ShouldPlaceFourteenUtcDaysOldestFirst_WithTodayLast()
    {
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        var counts = new Dictionary<DateOnly, int>
        {
            [today] = 4, [today.AddDays(-1)] = 2, [today.AddDays(-13)] = 1, [today.AddDays(-14)] = 99, [today.AddDays(1)] = 99,
        };

        var series = SourceOverviewRules.DailySeries(counts, Now);

        series.Should().Equal(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 4);
    }

    [Fact]
    public void SeriesStart_ShouldBeMidnightUtcThirteenDaysBeforeToday()
    {
        SourceOverviewRules.SeriesStart(Now).Should().Be(new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero));
        SourceOverviewRules.SeriesStart(new DateTimeOffset(2026, 11, 11, 1, 0, 0, TimeSpan.FromHours(5)))
            .Should().Be(new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero), "the day is the UTC day, 2026-11-10 20:00Z");
    }

    [Fact]
    public void IsHarvesting_ShouldRequireAnActivePipelineWithAnEnabledCaptureSchedule()
    {
        var pipeline = Pipeline(PipelineState.Active, new ScheduleModel
        {
            Id = Guid.NewGuid(), Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(6),
            TimeZone = PipelineConstants.DefaultTimeZone, Enabled = true,
        });

        SourceOverviewRules.IsHarvesting(pipeline).Should().BeTrue();
        SourceOverviewRules.IsHarvesting(pipeline with { State = PipelineState.Paused }).Should().BeFalse();
        SourceOverviewRules.IsHarvesting(pipeline with { State = PipelineState.PendingApproval }).Should().BeFalse();
        SourceOverviewRules.IsHarvesting(pipeline with { Schedules = [pipeline.Schedules[0] with { Enabled = false }] }).Should().BeFalse();
        SourceOverviewRules.IsHarvesting(pipeline with { Schedules = [pipeline.Schedules[0] with { Kind = WorkKind.Check }] }).Should().BeFalse();
    }

    [Fact]
    public void Health_ShouldReportTheLaterCurrentPause_AndTheWorstFailures()
    {
        var host = new SourceOverviewRules.HostState(5000, Now.AddMinutes(5), "Challenge", 1, Now.AddMinutes(30), "CaptchaRecaptcha", 4);

        var health = SourceOverviewRules.Health("www.afisha.uz", host, null, Now);

        health.Should().Match<SourceHealthModel>(value => value.HostKey == "www.afisha.uz" && value.MinDelayMs == 5000
            && value.PausedUntil == Now.AddMinutes(30) && value.PauseReason == "CaptchaRecaptcha" && value.ConsecutiveFailures == 4);
    }

    [Fact]
    public void Health_ShouldOmitAnEndedPause_AndDefaultThePaceOfAnUnknownHost()
    {
        var ended = new SourceOverviewRules.HostState(3000, Now.AddMinutes(-1), "RateLimited", 0, null, null, 0);
        var refusal = new SourceRefusalModel { At = Now.AddHours(-2), Outcome = FetchOutcome.RateLimited, Message = "429" };

        var health = SourceOverviewRules.Health("x.example", ended, refusal, Now);
        var unknown = SourceOverviewRules.Health("y.example", null, null, Now);

        health.PausedUntil.Should().BeNull();
        health.PauseReason.Should().BeNull();
        health.LastRefusal.Should().Be(refusal);
        unknown.MinDelayMs.Should().Be(SourceConstants.DefaultReadingDelayMs);
        unknown.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void MergeLimits_ShouldReplaceOnlyStatedValues_AndBoundMaxPages()
    {
        var stored = new PipelineLimits { MaxPages = 3, MaxItems = 100 };

        RegistrationRules.MergeLimits(stored, null).Limits.Should().Be(stored);
        RegistrationRules.MergeLimits(stored, new PipelineLimits { MaxPages = 50 }).Limits.Should().Be(stored with { MaxPages = 50 });
        RegistrationRules.MergeLimits(stored, new PipelineLimits { MaxPages = 51 }).Error.Should().NotBeNull();
        RegistrationRules.MergeLimits(stored, new PipelineLimits { MaxPages = 0 }).Error.Should().NotBeNull();
        RegistrationRules.MergeLimits(stored, new PipelineLimits { MaxItems = 0 }).Error.Should().NotBeNull();
    }

    private static SourceHarvestModel Harvest(RunStatus status) => new()
    {
        RunId = Guid.NewGuid(), PipelineId = Guid.NewGuid(), At = Now, Status = status, NewPosts = 4, Found = 23,
    };

    private static SourceSectionModel Section(bool harvesting) => new()
    {
        PipelineId = Guid.NewGuid(), Harvesting = harvesting, Paused = !harvesting, Posts = 0, Subscribers = 0,
    };

    private static PipelineModel Pipeline(PipelineState state, params ScheduleModel[] schedules) => new()
    {
        Id = Guid.NewGuid(), ContainerId = Guid.NewGuid(), SourceId = Guid.NewGuid(), ScopeKey = PipelineConstants.WholeScopeKey,
        Scope = new PipelineScope(), State = state, Pinned = false, Ai = AiMode.Off, Limits = new PipelineLimits(),
        Steps = new Dictionary<string, StepOverride>(), Schedules = schedules, CreatedAtUtc = Now,
    };
}
