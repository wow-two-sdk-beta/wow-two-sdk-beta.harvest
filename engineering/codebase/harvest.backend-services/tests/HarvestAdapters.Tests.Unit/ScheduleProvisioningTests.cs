using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters;

namespace HarvestAdapters.Tests.Unit;

/// <summary>Checks which cadences a profile turns on for a section that already exists: none for a pipeline whose schedules
/// were turned off automatically, and today's answer for every other.</summary>
public sealed class ScheduleProvisioningTests
{
    private static readonly ScheduleDeclaration[] Defaults =
    [
        new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1) },
        new() { Kind = WorkKind.Check, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(24) },
    ];

    [Fact]
    public void EnabledCadence_ShouldReturnNull_WhenThePipelineWasTurnedOffAutomatically()
    {
        var pipeline = Pipeline(Row(WorkKind.Capture, enabled: false), Row(WorkKind.Check, enabled: false)) with
        {
            AutoOff = new() { AtUtc = DateTimeOffset.UtcNow, RunId = Guid.NewGuid(), Failures = 10, Reason = "blocked" },
        };

        ScheduleProvisioning.EnabledCadence(pipeline, Defaults).Should().BeNull("the setup leaves a turned-off pipeline as it is");
        ScheduleProvisioning.EnabledCadence(pipeline with { Schedules = [] }, Defaults).Should().BeNull("a missing row is not added either");
    }

    [Fact]
    public void EnabledCadence_ShouldTurnOnTheDisabledRowsAndAddTheMissingOnes_WhenThePipelineHasNoAutoOff()
    {
        var pipeline = Pipeline(Row(WorkKind.Capture, enabled: false));

        var cadence = ScheduleProvisioning.EnabledCadence(pipeline, Defaults);

        cadence.Should().NotBeNull();
        cadence!.Select(row => (row.Kind, row.Enabled)).Should().BeEquivalentTo([(WorkKind.Capture, true), (WorkKind.Check, true)]);
    }

    [Fact]
    public void EnabledCadence_ShouldReturnNull_WhenEveryRowIsAlreadyOn()
    {
        var pipeline = Pipeline(Row(WorkKind.Capture, enabled: true), Row(WorkKind.Check, enabled: true));

        ScheduleProvisioning.EnabledCadence(pipeline, Defaults).Should().BeNull();
    }

    [Fact]
    public void EnabledCadence_ShouldKeepCaptureOff_WhenTheSectionRunsTheStagedPath()
    {
        var pipeline = Pipeline(Row(WorkKind.Capture, enabled: false), Row(WorkKind.Discover, enabled: true));

        var cadence = ScheduleProvisioning.EnabledCadence(pipeline, Defaults);

        cadence.Should().NotBeNull();
        cadence!.Single(row => row.Kind == WorkKind.Capture).Enabled.Should().BeFalse();
        cadence.Single(row => row.Kind == WorkKind.Check).Enabled.Should().BeTrue();
    }

    private static PipelineModel Pipeline(params ScheduleModel[] schedules) => new()
    {
        Id = Guid.NewGuid(), ContainerId = Guid.NewGuid(), SourceId = Guid.NewGuid(), ScopeKey = "*", Scope = new(),
        State = PipelineState.Active, Pinned = false, Ai = AiMode.Off, Limits = new(),
        Steps = new Dictionary<string, StepOverride>(), Schedules = schedules, CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    private static ScheduleModel Row(WorkKind kind, bool enabled) => new()
    {
        Id = Guid.NewGuid(), Kind = kind, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1),
        TimeZone = "Asia/Tashkent", Enabled = enabled,
    };
}
