using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Ocharo;
using HarvestSources.Api.Pipelines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Meta;

namespace HarvestSources.Tests.E2E;

/// <summary>The <c>pipelines</c> host verb: it queues a run, sets a host's reading pace and one schedule's interval, sets how
/// many failed runs turn a pipeline's schedules off and reads a pipeline, on the deployed host's own services with no
/// operator token; its discover and harvest kinds walk a whole catalog.</summary>
public sealed class PipelinesVerbTests
{
    [Fact]
    public async Task Run_queues_a_capture_run_by_default_and_prints_the_run_line()
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services);

        var run = await Run(host.Services, "run", "--pipeline", pipelineId.ToString("D"));

        run.Exit.Should().Be(0, run.Error);
        var line = LastLine(run.Output);
        line.GetProperty("pipelineId").GetGuid().Should().Be(pipelineId);
        line.GetProperty("kind").GetString().Should().Be("capture");
        line.GetProperty("status").GetString().Should().Be("queued");
        var stored = (await RunsAsync(host.Services, pipelineId)).Should().ContainSingle().Subject;
        stored.Id.Should().Be(line.GetProperty("runId").GetGuid());
        (stored.Kind, stored.Status).Should().Be((WorkKind.Capture, RunStatus.Queued));
    }

    [Fact]
    public async Task Run_names_its_kind_and_refuses_a_second_run_of_that_kind_while_one_is_queued()
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services);
        var first = await Run(host.Services, "run", "--pipeline", pipelineId.ToString("D"), "--kind", "capture");

        var second = await Run(host.Services, "run", "--pipeline", pipelineId.ToString("D"), "--kind", "CAPTURE");
        var again = await Run(host.Services, "run", "--pipeline", pipelineId.ToString("D"));
        var other = await Run(host.Services, "run", "--pipeline", pipelineId.ToString("D"), "--kind", "refresh");

        first.Exit.Should().Be(0, first.Error);
        LastLine(first.Output).GetProperty("status").GetString().Should().Be("queued");
        foreach (var refused in new[] { second, again })
        {
            refused.Exit.Should().Be(1);
            refused.Error.Should().Contain("pipelines run:").And.Contain("already has a capture run").And.Contain("queued");
            refused.Output.Should().BeEmpty();
        }
        other.Exit.Should().Be(0, "another kind is not blocked by the queued capture run");
        LastLine(other.Output).GetProperty("kind").GetString().Should().Be("refresh");
        (await RunsAsync(host.Services, pipelineId)).Select(item => item.Kind).Should().BeEquivalentTo([WorkKind.Capture, WorkKind.Refresh]);
    }

    [Fact]
    public async Task Run_is_refused_while_a_run_of_that_kind_is_running_and_allowed_again_once_it_has_finished()
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services);
        var first = await Run(host.Services, "run", "--pipeline", pipelineId.ToString("D"));
        string runId = LastLine(first.Output).GetProperty("runId").GetGuid().ToString("D");

        await SetStatusAsync(host.Services, runId, "running");
        var running = await Run(host.Services, "run", "--pipeline", pipelineId.ToString("D"));
        await SetStatusAsync(host.Services, runId, "completed");
        var finished = await Run(host.Services, "run", "--pipeline", pipelineId.ToString("D"));

        running.Exit.Should().Be(1);
        running.Error.Should().Contain("already has a capture run").And.Contain("running");
        finished.Exit.Should().Be(0, finished.Error);
        LastLine(finished.Output).GetProperty("runId").GetGuid().Should().NotBe(Guid.Parse(runId));
    }

    [Fact]
    public async Task Run_discover_queues_a_discover_run_and_refuses_a_second_while_it_is_queued()
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services);

        var first = await Run(host.Services, "run", "--pipeline", pipelineId.ToString("D"), "--kind", "discover");
        var second = await Run(host.Services, "run", "--pipeline", pipelineId.ToString("D"), "--kind", "Discover");
        var other = await Run(host.Services, "run", "--pipeline", pipelineId.ToString("D"), "--kind", "capture");

        first.Exit.Should().Be(0, first.Error);
        var line = LastLine(first.Output);
        line.GetProperty("pipelineId").GetGuid().Should().Be(pipelineId);
        line.GetProperty("kind").GetString().Should().Be("discover");
        line.GetProperty("status").GetString().Should().Be("queued");
        second.Exit.Should().Be(1);
        second.Error.Should().Contain("pipelines run:").And.Contain("already has a discover run").And.Contain("queued");
        second.Output.Should().BeEmpty();
        other.Exit.Should().Be(0, "another kind is not blocked by the queued discover run");
        var stored = await RunsAsync(host.Services, pipelineId);
        stored.Select(item => item.Kind).Should().BeEquivalentTo([WorkKind.Discover, WorkKind.Capture]);
        stored.Single(item => item.Kind == WorkKind.Discover).Id.Should().Be(line.GetProperty("runId").GetGuid());
    }

    [Fact]
    public async Task Run_harvest_starts_a_harvest_run_and_refuses_a_second_while_one_is_running()
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services);

        var first = await Run(host.Services, "run", "--pipeline", pipelineId.ToString("D"), "--kind", "harvest");
        string runId = LastLine(first.Output).GetProperty("runId").GetGuid().ToString("D");
        await SetStatusAsync(host.Services, runId, "running");
        var second = await Run(host.Services, "run", "--pipeline", pipelineId.ToString("D"), "--kind", "harvest");
        await SetStatusAsync(host.Services, runId, "completed");
        var again = await Run(host.Services, "run", "--pipeline", pipelineId.ToString("D"), "--kind", "harvest");

        first.Exit.Should().Be(0, first.Error);
        LastLine(first.Output).GetProperty("kind").GetString().Should().Be("harvest");
        var stored = (await RunsAsync(host.Services, pipelineId)).Should().Contain(item => item.Id.ToString("D") == runId).Subject;
        stored.Kind.Should().Be(WorkKind.Harvest);
        second.Exit.Should().Be(1);
        second.Error.Should().Contain("pipelines run:").And.Contain("already has a harvest run").And.Contain("running");
        second.Output.Should().BeEmpty();
        again.Exit.Should().Be(0, "a finished harvest run does not block the next");
        LastLine(again.Output).GetProperty("runId").GetGuid().Should().NotBe(Guid.Parse(runId));
    }

    [Fact]
    public async Task Run_of_a_pipeline_that_does_not_exist_is_refused()
    {
        await using var host = await StartAsync();

        var run = await Run(host.Services, "run", "--pipeline", Guid.NewGuid().ToString("D"));

        run.Exit.Should().Be(1);
        run.Error.Should().Contain("pipelines run:");
        run.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task Pace_sets_the_hosts_reading_pace_by_family_handle_or_id_and_prints_the_health_line()
    {
        await using var host = await StartAsync();
        await Ocharo(host.Services, "setup", "--group", "afisha");
        var afisha = (await OcharoSourcesAsync(host.Services)).Single(source => source.Handle == "www.afisha.uz");

        var byLabel = await Run(host.Services, "pace", "--source", "Site/WWW.Afisha.uz", "--ms", "15000");
        var byId = await Run(host.Services, "pace", "--source", afisha.Id.ToString("D"), "--ms", "1000");

        byLabel.Exit.Should().Be(0, byLabel.Error);
        var line = LastLine(byLabel.Output);
        line.GetProperty("sourceId").GetGuid().Should().Be(afisha.Id);
        line.GetProperty("host").GetString().Should().Be("www.afisha.uz");
        line.GetProperty("minDelayMs").GetInt32().Should().Be(15000);
        byId.Exit.Should().Be(0, byId.Error);
        LastLine(byId.Output).GetProperty("minDelayMs").GetInt32().Should().Be(1000);
        (await PaceAsync(host.Services, afisha)).Should().Be(1000);

        (await Run(host.Services, "pace", "--source", "site/www.afisha.uz", "--ms", "15000")).Exit.Should().Be(0);
        (await PaceAsync(host.Services, afisha)).Should().Be(15000);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("60001")]
    public async Task Pace_out_of_range_is_refused_and_leaves_the_pace(string ms)
    {
        await using var host = await StartAsync();
        await Ocharo(host.Services, "setup", "--group", "afisha");
        var afisha = (await OcharoSourcesAsync(host.Services)).Single(source => source.Handle == "www.afisha.uz");
        var before = await PaceAsync(host.Services, afisha);

        var run = await Run(host.Services, "pace", "--source", "site/www.afisha.uz", "--ms", ms);

        run.Exit.Should().Be(1);
        run.Error.Should().Contain("pipelines pace:").And.Contain("1000");
        run.Output.Should().BeEmpty();
        (await PaceAsync(host.Services, afisha)).Should().Be(before);
    }

    [Fact]
    public async Task Pace_of_an_unknown_source_or_tenant_is_refused()
    {
        await using var host = await StartAsync();
        await Ocharo(host.Services, "setup", "--group", "afisha");

        var source = await Run(host.Services, "pace", "--source", "site/nowhere.example", "--ms", "5000");
        var tenant = await Run(host.Services, "pace", "--source", "site/www.afisha.uz", "--ms", "5000", "--tenant", "no-such-tenant");

        source.Exit.Should().Be(1);
        source.Error.Should().Contain("No source matches");
        tenant.Exit.Should().Be(1);
        tenant.Error.Should().Contain("No tenant");
    }

    [Fact]
    public async Task A_family_handle_in_two_tenants_is_ambiguous_until_the_tenant_is_named()
    {
        await using var host = await StartAsync();
        await Ocharo(host.Services, "setup", "--group", "afisha");
        await Ocharo(host.Services, "setup", "--group", "afisha", "--tenant", "isolation-check");

        var ambiguous = await Run(host.Services, "pace", "--source", "site/www.afisha.uz", "--ms", "20000");
        var narrowed = await Run(host.Services, "pace", "--source", "site/www.afisha.uz", "--ms", "20000", "--tenant", "isolation-check");

        ambiguous.Exit.Should().Be(1);
        ambiguous.Error.Should().Contain("matches 2 sources");
        ambiguous.Output.Should().BeEmpty();
        narrowed.Exit.Should().Be(0, narrowed.Error);
        LastLine(narrowed.Output).GetProperty("minDelayMs").GetInt32().Should().Be(20000);
    }

    [Fact]
    public async Task Schedule_replaces_only_the_named_kinds_schedule_and_keeps_the_others_the_name_the_scope_and_the_limits()
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services, "--schedules");
        await TuneAsync(host.Services, pipelineId);
        var before = await ReadAsync(host.Services, pipelineId);

        var run = await Run(host.Services, "schedule", "--pipeline", pipelineId.ToString("D"), "--kind", "capture", "--every", "15m");

        run.Exit.Should().Be(0, run.Error);
        var after = await ReadAsync(host.Services, pipelineId);
        var capture = after.Schedules.Single(schedule => schedule.Kind == WorkKind.Capture);
        (capture.Mode, capture.Every, capture.Cron, capture.Enabled).Should().Be((ScheduleMode.Interval, (TimeSpan?)TimeSpan.FromMinutes(15), (string?)null, true));
        capture.TimeZone.Should().Be(before.Schedules.Single(schedule => schedule.Kind == WorkKind.Capture).TimeZone);
        after.Schedules.Where(schedule => schedule.Kind != WorkKind.Capture).Should()
            .BeEquivalentTo(before.Schedules.Where(schedule => schedule.Kind != WorkKind.Capture), "the other schedules, stages included, stay as stored");
        after.Name.Should().Be("Afisha for the verb test").And.Be(before.Name);
        after.Scope.Should().BeEquivalentTo(before.Scope);
        after.Limits.Should().BeEquivalentTo(before.Limits);
        after.Limits.MaxPages.Should().Be(7);
        after.Steps.Should().BeEquivalentTo(before.Steps);
        after.State.Should().Be(before.State);
        after.Schedules.Single(schedule => schedule.Kind == WorkKind.Harvest).Stage!.BatchSize.Should().Be(17);

        var line = LastLine(run.Output);
        line.GetProperty("pipelineId").GetGuid().Should().Be(pipelineId);
        var printed = line.GetProperty("schedules").EnumerateArray().ToDictionary(item => item.GetProperty("kind").GetString()!);
        printed.Keys.Should().BeEquivalentTo(after.Schedules.Select(schedule => schedule.Kind.ToString().ToLowerInvariant()));
        printed["capture"].GetProperty("mode").GetString().Should().Be("interval");
        printed["capture"].GetProperty("every").GetString().Should().Be("15m");
        printed["capture"].GetProperty("cron").ValueKind.Should().Be(JsonValueKind.Null);
        printed["capture"].GetProperty("enabled").GetBoolean().Should().BeTrue();
        printed["check"].GetProperty("every").GetString().Should().Be("24h");
    }

    [Fact]
    public async Task Schedule_off_disables_the_schedule_and_a_kind_without_one_gets_an_interval_schedule()
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services, "--schedules");
        var before = await ReadAsync(host.Services, pipelineId);

        var off = await Run(host.Services, "schedule", "--pipeline", pipelineId.ToString("D"), "--kind", "check", "--every", "2h30m", "--off");
        var added = await Run(host.Services, "schedule", "--pipeline", pipelineId.ToString("D"), "--kind", "refresh", "--every", "01:00:00");

        off.Exit.Should().Be(0, off.Error);
        added.Exit.Should().Be(0, added.Error);
        var after = await ReadAsync(host.Services, pipelineId);
        var check = after.Schedules.Single(schedule => schedule.Kind == WorkKind.Check);
        (check.Mode, check.Every, check.Enabled).Should().Be((ScheduleMode.Interval, (TimeSpan?)TimeSpan.FromMinutes(150), false));
        var refresh = after.Schedules.Single(schedule => schedule.Kind == WorkKind.Refresh);
        (refresh.Mode, refresh.Every, refresh.Enabled).Should().Be((ScheduleMode.Interval, (TimeSpan?)TimeSpan.FromHours(1), true));
        after.Schedules.Single(schedule => schedule.Kind == WorkKind.Capture).Should()
            .BeEquivalentTo(before.Schedules.Single(schedule => schedule.Kind == WorkKind.Capture));
        after.Schedules.Should().HaveCount(before.Schedules.Count + 1);
        var printed = LastLine(added.Output).GetProperty("schedules").EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "check");
        printed.GetProperty("every").GetString().Should().Be("2h30m");
        printed.GetProperty("enabled").GetBoolean().Should().BeFalse("the line reads the saved pipeline");
    }

    [Fact]
    public async Task Schedule_saves_the_discover_and_harvest_rows_and_keeps_every_other_row_the_name_the_scope_and_the_limits()
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services);
        await TuneAsync(host.Services, pipelineId);
        var before = await ReadAsync(host.Services, pipelineId);

        var discover = await Run(host.Services, "schedule", "--pipeline", pipelineId.ToString("D"), "--kind", "discover", "--every", "1h");
        var harvest = await Run(host.Services, "schedule", "--pipeline", pipelineId.ToString("D"), "--kind", "harvest", "--every", "20m");

        discover.Exit.Should().Be(0, discover.Error);
        harvest.Exit.Should().Be(0, harvest.Error);
        discover.Error.Should().Contain("discover of pipeline").And.Contain("runs every 1h");
        var after = await ReadAsync(host.Services, pipelineId);
        var saved = after.Schedules.Single(schedule => schedule.Kind == WorkKind.Discover);
        (saved.Mode, saved.Every, saved.Cron, saved.Enabled).Should().Be((ScheduleMode.Interval, (TimeSpan?)TimeSpan.FromHours(1), (string?)null, true));
        saved.Stage.Should().NotBeNull("a stage row carries its settings");
        var tuned = after.Schedules.Single(schedule => schedule.Kind == WorkKind.Harvest);
        (tuned.Mode, tuned.Every, tuned.Enabled).Should().Be((ScheduleMode.Interval, (TimeSpan?)TimeSpan.FromMinutes(20), true));
        tuned.Stage!.BatchSize.Should().Be(17, "the stored stage settings stay");
        after.Schedules.Where(schedule => schedule.Kind is not (WorkKind.Discover or WorkKind.Harvest)).Should()
            .BeEquivalentTo(before.Schedules.Where(schedule => schedule.Kind is not (WorkKind.Discover or WorkKind.Harvest)),
                "the other rows stay as stored");
        after.Name.Should().Be(before.Name);
        after.Scope.Should().BeEquivalentTo(before.Scope);
        after.Limits.Should().BeEquivalentTo(before.Limits);
        after.State.Should().Be(before.State);
        var printed = LastLine(harvest.Output).GetProperty("schedules").EnumerateArray().ToDictionary(item => item.GetProperty("kind").GetString()!);
        printed.Keys.Should().BeEquivalentTo(after.Schedules.Select(schedule => schedule.Kind.ToString().ToLowerInvariant()));
        printed["discover"].GetProperty("every").GetString().Should().Be("1h");
        printed["harvest"].GetProperty("every").GetString().Should().Be("20m");
        printed["harvest"].GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Schedule_off_disables_a_stage_row_and_leaves_the_other_stage_enabled()
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services);
        await Run(host.Services, "schedule", "--pipeline", pipelineId.ToString("D"), "--kind", "discover", "--every", "1h");
        await Run(host.Services, "schedule", "--pipeline", pipelineId.ToString("D"), "--kind", "harvest", "--every", "30m");

        var off = await Run(host.Services, "schedule", "--pipeline", pipelineId.ToString("D"), "--kind", "harvest", "--every", "45m", "--off");

        off.Exit.Should().Be(0, off.Error);
        off.Error.Should().Contain("harvest of pipeline").And.Contain("runs every 45m, off");
        var after = await ReadAsync(host.Services, pipelineId);
        var harvest = after.Schedules.Single(schedule => schedule.Kind == WorkKind.Harvest);
        (harvest.Mode, harvest.Every, harvest.Enabled).Should().Be((ScheduleMode.Interval, (TimeSpan?)TimeSpan.FromMinutes(45), false));
        after.Schedules.Single(schedule => schedule.Kind == WorkKind.Discover).Enabled.Should().BeTrue();
        var printed = LastLine(off.Output).GetProperty("schedules").EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "harvest");
        printed.GetProperty("enabled").GetBoolean().Should().BeFalse("the line reads the saved pipeline");
    }

    [Theory]
    [InlineData("discover")]
    [InlineData("harvest")]
    public async Task Schedule_of_a_stage_while_capture_is_enabled_is_refused_names_the_fix_and_saves_nothing(string kind)
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services, "--schedules");
        var before = await ReadAsync(host.Services, pipelineId);
        before.Schedules.Single(schedule => schedule.Kind == WorkKind.Capture).Enabled.Should().BeTrue();

        var run = await Run(host.Services, "schedule", "--pipeline", pipelineId.ToString("D"), "--kind", kind, "--every", "1h");

        run.Exit.Should().Be(1);
        run.Error.Should().Contain("pipelines schedule:")
            .And.Contain($"pipelines schedule --pipeline {pipelineId:D} --kind capture --every <d> --off");
        run.Output.Should().BeEmpty();
        (await ReadAsync(host.Services, pipelineId)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task A_catalog_walk_is_capture_off_then_discover_then_harvest_and_show_lists_the_three_rows()
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services, "--schedules");
        string id = pipelineId.ToString("D");

        var capture = await Run(host.Services, "schedule", "--pipeline", id, "--kind", "capture", "--every", "6h", "--off");
        var discover = await Run(host.Services, "schedule", "--pipeline", id, "--kind", "discover", "--every", "1h");
        var harvest = await Run(host.Services, "schedule", "--pipeline", id, "--kind", "harvest", "--every", "30m");
        var show = await Run(host.Services, "show", "--pipeline", id);

        foreach (var step in new[] { capture, discover, harvest, show })
            step.Exit.Should().Be(0, step.Error);
        var rows = LastLine(show.Output).GetProperty("schedules").EnumerateArray().ToDictionary(item => item.GetProperty("kind").GetString()!);
        rows["capture"].GetProperty("enabled").GetBoolean().Should().BeFalse();
        foreach (var (kind, every) in new[] { ("discover", "1h"), ("harvest", "30m") })
        {
            rows[kind].GetProperty("mode").GetString().Should().Be("interval");
            rows[kind].GetProperty("every").GetString().Should().Be(every);
            rows[kind].GetProperty("enabled").GetBoolean().Should().BeTrue();
            rows[kind].TryGetProperty("nextDueAtUtc", out _).Should().BeTrue("show lists the next due time of a stage row too");
        }
        var stored = await ReadAsync(host.Services, pipelineId);
        stored.Schedules.Where(schedule => schedule.Kind is WorkKind.Discover or WorkKind.Harvest).Should().OnlyContain(schedule => schedule.Enabled);
    }

    [Theory]
    [InlineData("4m")]
    [InlineData("0:04:59")]
    [InlineData("-01:00:00")]
    public async Task Schedule_under_five_minutes_is_refused_and_changes_nothing(string every)
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services, "--schedules");
        var before = await ReadAsync(host.Services, pipelineId);

        var run = await Run(host.Services, "schedule", "--pipeline", pipelineId.ToString("D"), "--kind", "capture", "--every", every);

        run.Exit.Should().Be(1);
        run.Error.Should().Contain("pipelines schedule:").And.Contain("5 minutes");
        run.Output.Should().BeEmpty();
        (await ReadAsync(host.Services, pipelineId)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Schedule_of_a_harvest_saves_the_interval_batch_gap_and_parallel_fetches_and_prints_them()
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services);
        string id = pipelineId.ToString("D");

        var harvest = await Run(host.Services, "schedule", "--pipeline", id, "--kind", "harvest", "--every", "15s",
            "--batch", "50", "--gap-ms", "250", "--parallel", "4");
        var discover = await Run(host.Services, "schedule", "--pipeline", id, "--kind", "discover", "--every", "5m", "--walk", "full");

        harvest.Exit.Should().Be(0, harvest.Error);
        harvest.Error.Should().Contain("harvest of pipeline").And.Contain("runs every 15s").And.Contain("batch 50, gap 250 ms, parallel 4");
        discover.Exit.Should().Be(0, discover.Error);
        discover.Error.Should().Contain("runs every 5m").And.Contain("walk full, gap host pace");
        var stored = (await ReadAsync(host.Services, pipelineId)).Schedules;
        var saved = stored.Single(schedule => schedule.Kind == WorkKind.Harvest);
        (saved.Every, saved.Enabled).Should().Be((TimeSpan.FromSeconds(15), true));
        (saved.Stage!.BatchSize, saved.Stage.GapMs, saved.Stage.Parallel).Should().Be((50, 250, 4));
        stored.Single(schedule => schedule.Kind == WorkKind.Discover).Stage!.Walk.Should().Be(DiscoveryWalk.Full);
        var rows = LastLine(discover.Output).GetProperty("schedules").EnumerateArray().ToDictionary(item => item.GetProperty("kind").GetString()!);
        var harvestStage = rows["harvest"].GetProperty("stage");
        harvestStage.GetProperty("batch").GetInt32().Should().Be(50);
        harvestStage.GetProperty("gapMs").GetInt32().Should().Be(250);
        harvestStage.GetProperty("parallel").GetInt32().Should().Be(4);
        harvestStage.GetProperty("walk").GetString().Should().Be("resume");
        harvestStage.GetProperty("disabled").GetBoolean().Should().BeFalse();
        rows["discover"].GetProperty("stage").GetProperty("walk").GetString().Should().Be("full");
        rows["discover"].GetProperty("stage").GetProperty("gapMs").GetString().Should().Be("host");
        rows["discover"].GetProperty("stage").GetProperty("parallel").GetInt32().Should().Be(1);
        rows["discover"].GetProperty("every").GetString().Should().Be("5m");
        var show = await Run(host.Services, "show", "--pipeline", id);
        var shown = LastLine(show.Output).GetProperty("schedules").EnumerateArray().ToDictionary(item => item.GetProperty("kind").GetString()!);
        shown["harvest"].GetProperty("stage").GetProperty("parallel").GetInt32().Should().Be(4);
        shown["discover"].GetProperty("stage").GetProperty("walk").GetString().Should().Be("full");
    }

    [Fact]
    public async Task Schedule_keeps_the_stored_settings_of_an_omitted_flag_and_gap_host_clears_the_gap()
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services);
        string id = pipelineId.ToString("D");
        await Run(host.Services, "schedule", "--pipeline", id, "--kind", "harvest", "--every", "1m", "--batch", "30", "--gap-ms", "500", "--parallel", "3");

        var later = await Run(host.Services, "schedule", "--pipeline", id, "--kind", "harvest", "--every", "2m");

        later.Exit.Should().Be(0, later.Error);
        var kept = (await ReadAsync(host.Services, pipelineId)).Schedules.Single(schedule => schedule.Kind == WorkKind.Harvest);
        (kept.Every, kept.Stage!.BatchSize, kept.Stage.GapMs, kept.Stage.Parallel).Should().Be((TimeSpan.FromMinutes(2), 30, 500, 3));

        var cleared = await Run(host.Services, "schedule", "--pipeline", id, "--kind", "harvest", "--every", "2m", "--gap-ms", "host");

        cleared.Exit.Should().Be(0, cleared.Error);
        cleared.Error.Should().Contain("gap host pace");
        var after = (await ReadAsync(host.Services, pipelineId)).Schedules.Single(schedule => schedule.Kind == WorkKind.Harvest);
        (after.Stage!.BatchSize, after.Stage.GapMs, after.Stage.Parallel).Should().Be((30, null, 3));
        LastLine(cleared.Output).GetProperty("schedules").EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "harvest")
            .GetProperty("stage").GetProperty("gapMs").GetString().Should().Be("host");
    }

    [Theory]
    [InlineData("harvest", "14s")]
    [InlineData("harvest", "0:00:14")]
    [InlineData("capture", "15s")]
    [InlineData("discover", "15s")]
    public async Task Schedule_under_its_floor_is_refused_and_a_harvest_floor_is_fifteen_seconds(string kind, string every)
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services);
        var before = await ReadAsync(host.Services, pipelineId);

        var run = await Run(host.Services, "schedule", "--pipeline", pipelineId.ToString("D"), "--kind", kind, "--every", every);

        run.Exit.Should().Be(1);
        run.Error.Should().Contain("pipelines schedule:").And.Contain(kind == "harvest" ? "15 seconds" : "5 minutes");
        run.Output.Should().BeEmpty();
        (await ReadAsync(host.Services, pipelineId)).Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData("harvest", "--batch", "501")]
    [InlineData("harvest", "--batch", "0")]
    [InlineData("harvest", "--parallel", "17")]
    [InlineData("harvest", "--parallel", "0")]
    [InlineData("harvest", "--gap-ms", "60001")]
    [InlineData("discover", "--gap-ms", "60001")]
    public async Task Schedule_with_a_stage_value_out_of_range_is_refused_and_saves_nothing(string kind, string flag, string value)
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services);
        var before = await ReadAsync(host.Services, pipelineId);

        var run = await Run(host.Services, "schedule", "--pipeline", pipelineId.ToString("D"), "--kind", kind, "--every", "1h", flag, value);

        run.Exit.Should().Be(1);
        run.Error.Should().Contain("pipelines schedule:");
        run.Output.Should().BeEmpty();
        (await ReadAsync(host.Services, pipelineId)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Schedule_of_a_pipeline_that_does_not_exist_is_refused()
    {
        await using var host = await StartAsync();

        var run = await Run(host.Services, "schedule", "--pipeline", Guid.NewGuid().ToString("D"), "--kind", "capture", "--every", "1h");

        run.Exit.Should().Be(1);
        run.Error.Should().Contain("pipelines schedule:");
    }

    [Fact]
    public async Task Show_prints_each_runs_bytes_and_the_last_24_hours_totals_without_the_older_run()
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services, "--schedules");
        string id = pipelineId.ToString("D");
        await Run(host.Services, "run", "--pipeline", id, "--kind", "refresh");
        await Run(host.Services, "run", "--pipeline", id, "--kind", "capture");
        await SqlAsync(host.Services, "UPDATE harvest_runs SET requests=7,bytes=1500 WHERE pipeline_id=@id AND kind='capture'", ("id", pipelineId));
        await SqlAsync(host.Services, "UPDATE harvest_runs SET requests=3,bytes=2500000000 WHERE pipeline_id=@id AND kind='refresh'", ("id", pipelineId));
        await SqlAsync(host.Services,
            """
            INSERT INTO harvest_runs (id,container_id,pipeline_id,kind,trigger,scheduled_for,status,requests,bytes,queued_at,started_at,finished_at)
            SELECT gen_random_uuid(),container_id,pipeline_id,'check','manual',now()-interval '30 hours','completed',100,999999,
                now()-interval '30 hours',now()-interval '30 hours',now()-interval '29 hours'
            FROM harvest_runs WHERE pipeline_id=@id AND kind='capture'
            """, ("id", pipelineId));
        var before = DateTime.UtcNow;

        var show = await Run(host.Services, "show", "--pipeline", id);

        show.Exit.Should().Be(0, show.Error);
        var line = LastLine(show.Output);
        var stored = await RunsAsync(host.Services, pipelineId);
        stored.Should().HaveCount(3);
        var rows = line.GetProperty("runs").EnumerateArray().ToArray();
        rows.Should().HaveCount(3);
        foreach (var row in rows)
            row.GetProperty("bytes").GetInt64().Should().Be(stored.Single(item => item.Id == row.GetProperty("runId").GetGuid()).Bytes);
        rows.Select(row => row.GetProperty("bytes").GetInt64()).Should().BeEquivalentTo([1500L, 2_500_000_000L, 999_999L]);
        var last = line.GetProperty("last24h");
        last.GetProperty("since").GetDateTime().ToUniversalTime().Should().BeCloseTo(before.AddHours(-24), TimeSpan.FromMinutes(5));
        last.GetProperty("runs").GetInt64().Should().Be(2);
        last.GetProperty("requests").GetInt64().Should().Be(10);
        last.GetProperty("bytes").GetInt64().Should().Be(2_500_001_500L);
        var kinds = last.GetProperty("byKind").EnumerateArray().ToDictionary(item => item.GetProperty("kind").GetString()!);
        kinds.Keys.Should().BeEquivalentTo(["capture", "refresh"]);
        kinds["capture"].GetProperty("runs").GetInt64().Should().Be(1);
        kinds["capture"].GetProperty("requests").GetInt64().Should().Be(7);
        kinds["capture"].GetProperty("bytes").GetInt64().Should().Be(1500);
        kinds["refresh"].GetProperty("requests").GetInt64().Should().Be(3);
        kinds["refresh"].GetProperty("bytes").GetInt64().Should().Be(2_500_000_000L);
    }

    [Fact]
    public async Task Show_prints_one_parseable_line_with_the_state_and_the_newest_runs_and_writes_nothing()
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services, "--schedules");
        await TuneAsync(host.Services, pipelineId);
        await Run(host.Services, "run", "--pipeline", pipelineId.ToString("D"), "--kind", "refresh");
        await Run(host.Services, "run", "--pipeline", pipelineId.ToString("D"), "--kind", "capture");
        var before = await ReadAsync(host.Services, pipelineId);

        var show = await Run(host.Services, "show", "--pipeline", pipelineId.ToString("D"));
        var newest = await Run(host.Services, "show", "--pipeline", pipelineId.ToString("D"), "--runs", "1");
        var none = await Run(host.Services, "show", "--pipeline", pipelineId.ToString("D"), "--runs", "0");

        show.Exit.Should().Be(0, show.Error);
        show.Output.TrimEnd().Split('\n').Should().ContainSingle("the verb prints one JSON line");
        var line = LastLine(show.Output);
        line.GetProperty("pipelineId").GetGuid().Should().Be(pipelineId);
        line.GetProperty("name").GetString().Should().Be(before.Name);
        line.GetProperty("state").GetString().Should().Be("active");
        line.GetProperty("limits").GetProperty("maxPages").GetInt32().Should().Be(7);
        line.GetProperty("limits").GetProperty("maxItems").GetInt32().Should().Be(before.Limits.MaxItems!.Value);
        line.GetProperty("schedules").EnumerateArray().Select(item => item.GetProperty("kind").GetString())
            .Should().BeEquivalentTo(before.Schedules.Select(schedule => schedule.Kind.ToString().ToLowerInvariant()));
        var runs = line.GetProperty("runs").EnumerateArray().ToArray();
        runs.Select(item => item.GetProperty("kind").GetString()).Should().Equal("capture", "refresh");
        runs.Select(item => item.GetProperty("status").GetString()).Should().Equal("queued", "completed");
        runs[1].GetProperty("completionReason").GetString().Should().Be("no_items", "a refresh of an empty frontier ends at once");
        runs[1].GetProperty("finishedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        runs[0].GetProperty("completionReason").ValueKind.Should().Be(JsonValueKind.Null);
        runs[0].EnumerateObject().Select(property => property.Name).Should().Equal("runId", "kind", "status", "fetchOutcome",
            "completionReason", "requests", "bytes", "captured", "newPosts", "failed", "startedAtUtc", "finishedAtUtc");
        show.Output.Should().NotContain("http", "no scope URL or address is printed");
        LastLine(newest.Output).GetProperty("runs").GetArrayLength().Should().Be(1);
        LastLine(none.Output).GetProperty("runs").GetArrayLength().Should().Be(0);
        (await ReadAsync(host.Services, pipelineId)).Should().BeEquivalentTo(before);
        (await RunsAsync(host.Services, pipelineId)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Show_of_a_pipeline_that_does_not_exist_is_refused()
    {
        await using var host = await StartAsync();

        var show = await Run(host.Services, "show", "--pipeline", Guid.NewGuid().ToString("D"));

        show.Exit.Should().Be(1);
        show.Error.Should().Contain("pipelines show:");
        show.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task Auto_off_sets_the_pipelines_own_count_clears_it_back_to_the_hosts_and_prints_the_line()
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services, "--schedules");
        string id = pipelineId.ToString("D");

        var three = await Run(host.Services, "auto-off", "--pipeline", id, "--after", "3");
        three.Exit.Should().Be(0, three.Error);
        var line = LastLine(three.Output);
        line.GetProperty("pipelineId").GetGuid().Should().Be(pipelineId);
        line.EnumerateObject().Select(property => property.Name).Should().Equal("pipelineId", "autoOff");
        var autoOff = line.GetProperty("autoOff");
        autoOff.EnumerateObject().Select(property => property.Name).Should().Equal("after", "fromDefault", "failures", "off");
        (autoOff.GetProperty("after").GetInt32(), autoOff.GetProperty("fromDefault").GetBoolean(), autoOff.GetProperty("failures").GetInt32())
            .Should().Be((3, false, 0));
        autoOff.GetProperty("off").ValueKind.Should().Be(JsonValueKind.Null);
        three.Error.Should().Contain($"pipeline {id} turns its harvesting schedules off after 3 failed runs in a row; 0 so far")
            .And.NotContain("host default");
        (await ScalarAsync<int?>(host.Services, "SELECT auto_off_after FROM harvest_pipelines WHERE id=@id", ("id", pipelineId))).Should().Be(3);

        var never = await Run(host.Services, "auto-off", "--pipeline", id, "--after", "0");
        never.Exit.Should().Be(0, never.Error);
        LastLine(never.Output).GetProperty("autoOff").GetProperty("after").GetInt32().Should().Be(0);
        never.Error.Should().Contain($"pipeline {id} never turns its harvesting schedules off");
        (await ScalarAsync<int?>(host.Services, "SELECT auto_off_after FROM harvest_pipelines WHERE id=@id", ("id", pipelineId))).Should().Be(0);

        var fallback = await Run(host.Services, "auto-off", "--pipeline", id, "--after", "default");
        fallback.Exit.Should().Be(0, fallback.Error);
        var cleared = LastLine(fallback.Output).GetProperty("autoOff");
        (cleared.GetProperty("after").GetInt32(), cleared.GetProperty("fromDefault").GetBoolean()).Should().Be((10, true));
        fallback.Error.Should().Contain("turns its harvesting schedules off after 10 failed runs in a row (host default); 0 so far");
        (await ScalarAsync<int?>(host.Services, "SELECT auto_off_after FROM harvest_pipelines WHERE id=@id", ("id", pipelineId))).Should().BeNull();
        (await ReadAsync(host.Services, pipelineId)).Schedules.Should().OnlyContain(schedule => schedule.Enabled || schedule.Kind != WorkKind.Capture,
            "setting the count never turns a row off");
    }

    [Fact]
    public async Task Auto_off_of_a_pipeline_that_does_not_exist_is_refused()
    {
        await using var host = await StartAsync();

        var run = await Run(host.Services, "auto-off", "--pipeline", Guid.NewGuid().ToString("D"), "--after", "3");

        run.Exit.Should().Be(1);
        run.Error.Should().Contain("pipelines auto-off:");
        run.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task Show_prints_the_auto_off_after_the_schedules_with_the_failed_runs_in_a_row()
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services, "--schedules");
        string id = pipelineId.ToString("D");
        foreach (var _ in new[] { 1, 2 })
        {
            var queued = await Run(host.Services, "run", "--pipeline", id);
            await SetStatusAsync(host.Services, LastLine(queued.Output).GetProperty("runId").GetGuid().ToString("D"), "failed");
        }
        await Run(host.Services, "run", "--pipeline", id);

        var show = await Run(host.Services, "show", "--pipeline", id);

        show.Exit.Should().Be(0, show.Error);
        var line = LastLine(show.Output);
        line.EnumerateObject().Select(property => property.Name).Should().Equal("pipelineId", "name", "state", "pauseReason", "limits",
            "schedules", "autoOff", "last24h", "runs");
        var autoOff = line.GetProperty("autoOff");
        (autoOff.GetProperty("after").GetInt32(), autoOff.GetProperty("fromDefault").GetBoolean(), autoOff.GetProperty("failures").GetInt32())
            .Should().Be((10, true, 2), "the queued run says nothing yet");
        autoOff.GetProperty("off").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("discover")]
    public async Task Show_prints_a_record_set_by_sql_and_schedule_turning_a_row_on_clears_it_and_says_so(string kind)
    {
        await using var host = await StartAsync();
        var pipelineId = await PipelineAsync(host.Services, "--schedules");
        string id = pipelineId.ToString("D");
        var queued = await Run(host.Services, "run", "--pipeline", id);
        var runId = LastLine(queued.Output).GetProperty("runId").GetGuid();
        await SetStatusAsync(host.Services, runId.ToString("D"), "failed");
        await SqlAsync(host.Services,
            """
            UPDATE harvest_schedules SET enabled=false,next_due_at=NULL WHERE pipeline_id=@id;
            UPDATE harvest_pipelines SET auto_off_at=now(),auto_off_run_id=@run,auto_off_failures=10,auto_off_reason='blocked' WHERE id=@id
            """, ("id", pipelineId), ("run", runId));

        var shown = LastLine((await Run(host.Services, "show", "--pipeline", id)).Output).GetProperty("autoOff");
        var off = shown.GetProperty("off");
        off.EnumerateObject().Select(property => property.Name).Should().Equal("atUtc", "runId", "failures", "reason");
        (off.GetProperty("runId").GetGuid(), off.GetProperty("failures").GetInt32(), off.GetProperty("reason").GetString())
            .Should().Be((runId, 10, "blocked"));
        off.GetProperty("atUtc").ValueKind.Should().Be(JsonValueKind.String);
        shown.GetProperty("failures").GetInt32().Should().Be(1);
        (await ReadAsync(host.Services, pipelineId)).AutoOff.Should().NotBeNull();

        var schedule = await Run(host.Services, "schedule", "--pipeline", id, "--kind", kind, "--every", "1h");

        schedule.Exit.Should().Be(0, schedule.Error);
        schedule.Error.Should().EndWith("; the automatic turn-off is cleared and its failed-run count starts again" + Environment.NewLine);
        (await ReadAsync(host.Services, pipelineId)).AutoOff.Should().BeNull();
        var after = LastLine((await Run(host.Services, "show", "--pipeline", id)).Output).GetProperty("autoOff");
        after.GetProperty("off").ValueKind.Should().Be(JsonValueKind.Null);
        after.GetProperty("failures").GetInt32().Should().Be(0, "the failed run was queued before the row was turned on");
        var again = await Run(host.Services, "schedule", "--pipeline", id, "--kind", kind, "--every", "2h");
        again.Error.Should().NotContain("automatic turn-off");
    }

    [Theory]
    [InlineData("1h", 3600)]
    [InlineData("15m", 900)]
    [InlineData("2h30m", 9000)]
    [InlineData("1d", 86400)]
    [InlineData("1D2H", 93600)]
    [InlineData("90s", 90)]
    [InlineData("01:00:00", 3600)]
    [InlineData("1.00:00:00", 86400)]
    public void A_duration_reads_as_units_or_a_timespan(string text, int seconds)
    {
        PipelinesVerb.TryParseDuration(text, out var duration).Should().BeTrue();
        duration.Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Theory]
    [InlineData("")]
    [InlineData("15")]
    [InlineData("m15")]
    [InlineData("1h 30m")]
    [InlineData("0m")]
    [InlineData("1x")]
    [InlineData("99999999999h")]
    public void Anything_else_is_not_a_duration(string text) => PipelinesVerb.TryParseDuration(text, out _).Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("list")]
    [InlineData("run")]
    [InlineData("run --kind capture")]
    [InlineData("run --pipeline not-a-guid")]
    [InlineData("run --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind sweep")]
    [InlineData("run --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind")]
    [InlineData("run --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11")]
    [InlineData("run --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --ms 5000")]
    [InlineData("pace")]
    [InlineData("pace --source site/www.afisha.uz")]
    [InlineData("pace --ms 15000")]
    [InlineData("pace --source site/www.afisha.uz --ms fast")]
    [InlineData("pace --source site/www.afisha.uz --ms -5")]
    [InlineData("pace --source site/www.afisha.uz --ms 15000 --kind capture")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --every 1h")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind capture")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind capture --every soon")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind capture --every 1h --off --off")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind sweep --every 1h")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind process --every 1h")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind harvest --every 1h --walk full")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind capture --every 1h --walk full")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind discover --every 1h --walk sideways")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind discover --every 1h --batch 5")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind capture --every 1h --batch 5")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind discover --every 1h --parallel 2")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind capture --every 1h --parallel 2")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind capture --every 1h --gap-ms 100")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind harvest --every 1h --gap-ms fast")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind harvest --every 1h --gap-ms -5")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind harvest --every 1h --batch many")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind harvest --every 1h --parallel 2 --parallel 3")]
    [InlineData("schedule --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --kind harvest --every 1h --parallel")]
    [InlineData("show --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --batch 5")]
    [InlineData("auto-off")]
    [InlineData("auto-off --after 3")]
    [InlineData("auto-off --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11")]
    [InlineData("auto-off --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --after -1")]
    [InlineData("auto-off --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --after 1001")]
    [InlineData("auto-off --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --after many")]
    [InlineData("auto-off --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --after 3 --kind capture")]
    [InlineData("show")]
    [InlineData("show --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --runs 21")]
    [InlineData("show --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --runs many")]
    [InlineData("show --pipeline 5b1f3a52-6f5e-4c3e-9c0f-0d6e4a1f7a11 --off")]
    public async Task Usage_is_answered_before_any_service_is_resolved(string commandLine)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();

        var run = await Run(services, commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        run.Exit.Should().Be(2);
        run.Error.Should().Contain("Usage: pipelines");
        run.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task The_verb_answers_its_usage_through_the_service_host_without_serving()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await ServiceHost.RunAsync(["pipelines", "show"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DB_CONNECTION"] = "Host=127.0.0.1;Port=1;Database=unreachable;Username=synthetic;Timeout=1",
                });
                builder.Configure();
            },
            _ => throw new InvalidOperationException("A verb must not start the source host."),
            verbs => verbs.AddHostVerb(PipelinesVerb.Name, (services, verb, ct) =>
                PipelinesVerb.RunAsync(services, verb.Arguments, verb.Output, verb.Error, ct)),
            output, error, CancellationToken.None);

        exit.Should().Be(2, error.ToString());
        error.ToString().Should().Contain("Usage: pipelines");
        output.ToString().Should().BeEmpty();
    }

    private static JsonElement LastLine(string output)
    {
        using var document = JsonDocument.Parse(output.TrimEnd().Split('\n')[^1]);
        return document.RootElement.Clone();
    }

    /// <summary>Sets up the Ocharo Afisha channels and returns the pipeline of the first.</summary>
    private static async Task<Guid> PipelineAsync(IServiceProvider services, params string[] extra)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await OcharoSetupVerb.RunAsync(services, ["setup", "--group", "afisha", .. extra], output, error, CancellationToken.None);
        exit.Should().Be(0, error.ToString());
        return LastLine(output.ToString()).GetProperty("subscriptions")[0].GetProperty("pipelineId").GetGuid();
    }

    /// <summary>Names the pipeline and sets a page limit, and adds a disabled harvest stage with its own settings, so a schedule
    /// edit has a name, a limit and a stage to keep; an enabled harvest stage would conflict with the capture schedule.</summary>
    private static async Task TuneAsync(IServiceProvider services, Guid pipelineId)
    {
        using var scope = services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var original = Value(await registry.GetPipelineAsync(pipelineId));
        Value(await registry.UpdatePipelineAsync(pipelineId, new()
        {
            Name = "Afisha for the verb test",
            Scope = original.Scope,
            Schedules =
            [
                .. original.Schedules.Select(schedule => new ScheduleDeclaration
                {
                    Kind = schedule.Kind, Mode = schedule.Mode, Every = schedule.Every, MaxEvery = schedule.MaxEvery, Cron = schedule.Cron,
                    TimeZone = schedule.TimeZone, Window = schedule.Window, Enabled = schedule.Enabled,
                    Stage = schedule.Stage,
                }),
                new() { Kind = WorkKind.Harvest, Mode = ScheduleMode.Interval, Every = TimeSpan.FromMinutes(30), Enabled = false, Stage = new() { BatchSize = 17 } },
            ],
            Limits = new() { MaxPages = 7 },
        }));
    }

    private static async Task<PipelineModel> ReadAsync(IServiceProvider services, Guid pipelineId)
    {
        using var scope = services.CreateScope();
        return Value(await scope.ServiceProvider.GetRequiredService<IRegistrationBroker>().GetPipelineAsync(pipelineId));
    }

    private static async Task<IReadOnlyList<RunModel>> RunsAsync(IServiceProvider services, Guid pipelineId)
    {
        using var scope = services.CreateScope();
        return Value(await scope.ServiceProvider.GetRequiredService<IRunBroker>().ListAsync(pipelineId));
    }

    private static async Task SetStatusAsync(IServiceProvider services, string runId, string status)
    {
        await using var connection = new NpgsqlConnection(services.GetRequiredService<IConfiguration>()["DatabaseSettings:ConnectionString"]);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE harvest_runs SET status=@status WHERE id=@id", connection);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("id", Guid.Parse(runId));
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private static async Task<int> SqlAsync(IServiceProvider services, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(services.GetRequiredService<IConfiguration>()["DatabaseSettings:ConnectionString"]);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<T?> ScalarAsync<T>(IServiceProvider services, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(services.GetRequiredService<IConfiguration>()["DatabaseSettings:ConnectionString"]);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        var scalar = await command.ExecuteScalarAsync();
        return scalar is null or DBNull ? default : (T)scalar;
    }

    private static async Task<int> PaceAsync(IServiceProvider services, SourceModel source)
    {
        using var scope = services.CreateScope();
        var overview = Value(await scope.ServiceProvider.GetRequiredService<ISourceOverviewBroker>().ListOverviewAsync(source.ContainerId));
        return overview.Single(item => item.SourceId == source.Id).Health.MinDelayMs;
    }

    private static async Task<List<SourceModel>> OcharoSourcesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var tenant = Value(await registry.ListTenantsAsync()).Single(item => item.Slug == "ocharo");
        var sources = new List<SourceModel>();
        foreach (var container in Value(await registry.ListContainersAsync(tenant.Id)))
            sources.AddRange(Value(await registry.ListSourcesAsync(container.Id)));
        return sources;
    }

    private static async Task Ocharo(IServiceProvider services, params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await OcharoSetupVerb.RunAsync(services, arguments, output, error, CancellationToken.None);
        exit.Should().Be(0, error.ToString());
    }

    private static T Value<T>(Result<T> result) where T : notnull => Assert.IsType<Result<T>.Success>(result).Value;

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Development",
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseSettings:ConnectionString"] = await SourcePostgres.NewDatabaseAsync(),
            ["Harvest:Operator:ApiKey"] = "hv_" + new string('p', 32),
            ["Harvest:Worker:Enabled"] = "false",
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
        });
        builder.WebHost.UseTestServer();
        builder.Configure();
        var host = builder.Build();
        await host.ConfigureAsync();
        return host;
    }

    private static async Task<(int Exit, string Output, string Error)> Run(IServiceProvider services, params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await PipelinesVerb.RunAsync(services, arguments, output, error, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }
}
