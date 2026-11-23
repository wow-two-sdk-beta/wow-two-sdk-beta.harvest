using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Ocharo;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Meta;

namespace HarvestSources.Tests.E2E;

public sealed class OcharoSetupVerbTests
{
    [Fact]
    public async Task Setup_provisions_the_tenant_and_subscriptions_and_prints_their_ids_last()
    {
        await using var host = await StartAsync();

        var first = await Run(host.Services, ["setup", "--group", "afisha", "--run"]);
        var again = await Run(host.Services, ["setup", "--group", "afisha"]);

        first.Exit.Should().Be(0, first.Error);
        var result = JsonDocument.Parse(first.Output.TrimEnd().Split('\n')[^1]).RootElement;
        var subscriptions = result.GetProperty("subscriptions").EnumerateArray().ToArray();
        subscriptions.Should().NotBeEmpty().And.OnlyContain(item => item.GetProperty("groupId").GetString() == "afisha");
        result.GetProperty("runs").GetArrayLength().Should().Be(subscriptions.Length, "--run queues one capture per pipeline");
        again.Exit.Should().Be(0, again.Error);
        var repeated = JsonDocument.Parse(again.Output.TrimEnd().Split('\n')[^1]).RootElement;
        repeated.GetProperty("tenantId").GetGuid().Should().Be(result.GetProperty("tenantId").GetGuid());
        repeated.GetProperty("subscriptions").EnumerateArray().Select(item => item.GetProperty("subscriptionId").GetGuid())
            .Should().BeEquivalentTo(subscriptions.Select(item => item.GetProperty("subscriptionId").GetGuid()));
        repeated.GetProperty("runs").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Instagram_is_explicit_idempotent_and_keeps_schedules_off_when_capture_is_queued()
    {
        await using var host = await StartAsync();

        (await Run(host.Services, ["setup", "--group", "teplo"])).Exit.Should().Be(1);
        var first = await Run(host.Services, ["setup", "--group", "teplo", "--instagram", "--schedules", "--run"]);
        var again = await Run(host.Services, ["setup", "--group", "teplo", "--instagram", "--schedules"]);

        first.Exit.Should().Be(0, first.Error);
        again.Exit.Should().Be(0, again.Error);
        using var firstJson = JsonDocument.Parse(first.Output.TrimEnd().Split('\n')[^1]);
        using var againJson = JsonDocument.Parse(again.Output.TrimEnd().Split('\n')[^1]);
        var subscriptions = firstJson.RootElement.GetProperty("subscriptions").EnumerateArray().ToArray();
        subscriptions.Should().HaveCount(3).And.OnlyContain(item => item.GetProperty("groupId").GetString() == "teplo");
        firstJson.RootElement.GetProperty("runs").GetArrayLength().Should().Be(3);
        againJson.RootElement.GetProperty("runs").GetArrayLength().Should().Be(0);
        againJson.RootElement.GetProperty("subscriptions").EnumerateArray().Select(item => item.GetProperty("pipelineId").GetGuid())
            .Should().BeEquivalentTo(subscriptions.Select(item => item.GetProperty("pipelineId").GetGuid()));
        using var scope = host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        foreach (JsonElement subscription in subscriptions)
        {
            var pipeline = Assert.IsType<Result<PipelineModel>.Success>(
                await registry.GetPipelineAsync(subscription.GetProperty("pipelineId").GetGuid())).Value;
            pipeline.Schedules.Should().OnlyContain(schedule => !schedule.Enabled);
            pipeline.Schedules.Should().ContainSingle().Which.Should().Match<ScheduleModel>(schedule => schedule.Kind == WorkKind.Capture
                && schedule.Mode == ScheduleMode.Cron && schedule.Cron == "0 1,7,13,19 * * *" && schedule.TimeZone == "Asia/Tashkent");
        }
    }

    [Fact]
    public async Task Selected_Instagram_publishers_switch_between_hourly_collection_and_manual_tests()
    {
        await using var host = await StartAsync();
        var original = await Run(host.Services, ["setup", "--group", "afisha", "--schedules", "--selector"]);
        original.Exit.Should().Be(0, original.Error);
        var hourly = await Run(host.Services,
            ["setup", "--group", "afisha", "--group", "art-time", "--instagram-hourly", "--model-processing", "auto", "--selector"]);
        hourly.Exit.Should().Be(0, hourly.Error);
        using var json = JsonDocument.Parse(hourly.Output.TrimEnd().Split('\n')[^1]);
        var registered = json.RootElement.GetProperty("subscriptions").EnumerateArray().ToArray();
        registered.Should().HaveCount(2);
        json.RootElement.GetProperty("selectorVersion").GetInt32().Should().Be(2);

        using var scope = host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        Guid pipelineId = registered.Single(row => row.GetProperty("groupId").GetString() == "art-time").GetProperty("pipelineId").GetGuid();
        var pipeline = Assert.IsType<Result<PipelineModel>.Success>(await registry.GetPipelineAsync(pipelineId)).Value;
        pipeline.Limits.MaxItems.Should().Be(50);
        pipeline.Schedules.Should().ContainSingle().Which.Should().Match<ScheduleModel>(schedule =>
            schedule.Kind == WorkKind.Capture && schedule.Enabled && schedule.Cron == "3 * * * *" && schedule.TimeZone == "Asia/Tashkent");
        Assert.IsType<Result<SourceModel>.Success>(await registry.GetSourceAsync(pipeline.SourceId)).Value.ModelProcessing
            .Should().Be(ModelProcessingMode.Auto);

        var manual = await Run(host.Services,
            ["setup", "--group", "art-time", "--instagram-manual", "--model-processing", "manual"]);
        manual.Exit.Should().Be(0, manual.Error);
        Assert.IsType<Result<PipelineModel>.Success>(await registry.GetPipelineAsync(pipelineId)).Value.Schedules
            .Should().OnlyContain(schedule => !schedule.Enabled);
        Assert.IsType<Result<SourceModel>.Success>(await registry.GetSourceAsync(pipeline.SourceId)).Value.ModelProcessing
            .Should().Be(ModelProcessingMode.Manual);
        var sourceRows = Assert.IsType<Result<IReadOnlyList<SourceModel>>.Success>(
            await registry.ListSourcesAsync(pipeline.ContainerId)).Value;
        sourceRows.Where(source => source.Family != SourceFamily.Instagram)
            .Should().OnlyContain(source => source.ModelProcessing == ModelProcessingMode.Off);
    }

    [Fact]
    public async Task Instagram_refresh_is_rejected_before_resolving_or_mutating_services()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();

        var result = await Run(services, ["setup", "--instagram", "--run", "--refresh"]);

        result.Exit.Should().Be(2);
        result.Error.Should().Contain("--instagram cannot be combined with --refresh");
        result.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task Setup_with_refresh_queues_one_refresh_run_per_pipeline()
    {
        await using var host = await StartAsync();

        var refreshed = await Run(host.Services, ["setup", "--group", "afisha", "--refresh", "--refresh"]);

        refreshed.Exit.Should().Be(0, refreshed.Error);
        var result = JsonDocument.Parse(refreshed.Output.TrimEnd().Split('\n')[^1]).RootElement;
        var runs = result.GetProperty("runs").EnumerateArray().ToArray();
        runs.Should().HaveCount(result.GetProperty("subscriptions").GetArrayLength())
            .And.OnlyContain(item => item.GetProperty("kind").GetString() == "refresh");
    }

    [Fact]
    public async Task Status_reads_each_channels_cadence_in_one_short_line_and_writes_nothing()
    {
        await using var host = await StartAsync();

        var missing = await Run(host.Services, ["status", "--group", "afisha"]);
        await Run(host.Services, ["setup", "--group", "afisha", "--schedules"]);
        var status = await Run(host.Services, ["status", "--group", "afisha"]);
        var again = await Run(host.Services, ["status", "--group", "afisha"]);

        missing.Exit.Should().Be(1);
        status.Exit.Should().Be(0, status.Error);
        string last = status.Output.TrimEnd().Split('\n')[^1];
        last.Length.Should().BeLessThan(500);
        var result = JsonDocument.Parse(last).RootElement;
        result.GetProperty("tenant").GetString().Should().Be("ocharo");
        var lines = result.GetProperty("pipelines").EnumerateArray().Select(item => item.GetString()!).ToArray();
        lines.Should().Contain("afisha/instagram none");
        lines.Where(line => line != "afisha/instagram none").Should().NotBeEmpty().And.OnlyContain(line => line.StartsWith("afisha/") && line.Contains("capture cron due ")
            && line.Contains("check 1d due "));
        again.Output.Should().Be(status.Output, "status reads and never queues or writes");
        (await Run(host.Services, ["status", "--force"])).Exit.Should().Be(2);
    }

    [Fact]
    public async Task Setup_with_schedules_gives_a_pipeline_without_schedules_the_cron_capture_and_the_daily_check()
    {
        await using var host = await StartAsync();

        var setup = await Run(host.Services, ["setup", "--group", "afisha", "--schedules"]);

        setup.Exit.Should().Be(0, setup.Error);
        using var result = JsonDocument.Parse(setup.Output.TrimEnd().Split('\n')[^1]);
        var pipelineIds = result.RootElement.GetProperty("subscriptions").EnumerateArray()
            .Select(item => item.GetProperty("pipelineId").GetGuid()).ToArray();
        pipelineIds.Should().NotBeEmpty();
        using var scope = host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        foreach (Guid id in pipelineIds)
        {
            var pipeline = Assert.IsType<Result<PipelineModel>.Success>(await registry.GetPipelineAsync(id)).Value;
            var capture = pipeline.Schedules.Single(row => row.Kind == WorkKind.Capture);
            (capture.Mode, capture.Cron, capture.Every, capture.TimeZone, capture.Enabled)
                .Should().Be((ScheduleMode.Cron, "0 1,7,13,19 * * *", (TimeSpan?)null, "Asia/Tashkent", true));
            var check = pipeline.Schedules.Single(row => row.Kind == WorkKind.Check);
            (check.Mode, check.Every, check.Enabled).Should().Be((ScheduleMode.Interval, (TimeSpan?)TimeSpan.FromHours(24), true));
        }
    }

    [Fact]
    public async Task Setup_with_schedules_keeps_tuned_stages_and_does_not_enable_capture()
    {
        await using var host = await StartAsync();
        var initial = await Run(host.Services, ["setup", "--group", "afisha"]);
        initial.Exit.Should().Be(0, initial.Error);
        using var result = JsonDocument.Parse(initial.Output.TrimEnd().Split('\n')[^1]);
        Guid pipelineId = result.RootElement.GetProperty("subscriptions")[0].GetProperty("pipelineId").GetGuid();
        using var scope = host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var original = Assert.IsType<Result<PipelineModel>.Success>(await registry.GetPipelineAsync(pipelineId)).Value;
        Assert.IsType<Result<PipelineModel>.Success>(await registry.UpdatePipelineAsync(pipelineId, new()
        {
            Name = original.Name,
            Scope = original.Scope,
            Schedules =
            [
                new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(12), Enabled = false },
                new() { Kind = WorkKind.Harvest, Mode = ScheduleMode.Interval, Every = TimeSpan.FromMinutes(30), Stage = new() { BatchSize = 17 } },
            ],
        }));

        var setup = await Run(host.Services, ["setup", "--group", "afisha", "--schedules"]);
        setup.Exit.Should().Be(0, setup.Error);
        var again = await Run(host.Services, ["setup", "--group", "afisha", "--schedules"]);
        again.Exit.Should().Be(0, again.Error);
        var saved = Assert.IsType<Result<PipelineModel>.Success>(await registry.GetPipelineAsync(pipelineId)).Value;
        var capture = saved.Schedules.Single(row => row.Kind == WorkKind.Capture);
        capture.Enabled.Should().BeFalse();
        capture.Every.Should().Be(TimeSpan.FromHours(12));
        var harvest = saved.Schedules.Single(row => row.Kind == WorkKind.Harvest);
        harvest.Enabled.Should().BeTrue();
        harvest.Every.Should().Be(TimeSpan.FromMinutes(30));
        harvest.Stage!.BatchSize.Should().Be(17);
        saved.Schedules.Single(row => row.Kind == WorkKind.Check).Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Setup_gives_another_tenant_its_own_container_and_subscriptions()
    {
        await using var host = await StartAsync();

        var ocharo = await Run(host.Services, ["setup", "--group", "afisha"]);
        var probe = await Run(host.Services, ["setup", "--group", "afisha", "--tenant", "isolation-check"]);

        probe.Exit.Should().Be(0, probe.Error);
        var owner = JsonDocument.Parse(ocharo.Output.TrimEnd().Split('\n')[^1]).RootElement;
        var other = JsonDocument.Parse(probe.Output.TrimEnd().Split('\n')[^1]).RootElement;
        other.GetProperty("tenantId").GetGuid().Should().NotBe(owner.GetProperty("tenantId").GetGuid());
        other.GetProperty("containerId").GetGuid().Should().NotBe(owner.GetProperty("containerId").GetGuid());
        other.GetProperty("subscriptions").EnumerateArray().Select(item => item.GetProperty("subscriptionId").GetGuid())
            .Should().NotIntersectWith(owner.GetProperty("subscriptions").EnumerateArray().Select(item => item.GetProperty("subscriptionId").GetGuid()));
        (await Run(host.Services, ["setup", "--tenant", "Not A Slug"])).Exit.Should().Be(1);
    }

    [Fact]
    public async Task Setup_with_model_processing_sets_new_and_existing_sources_and_without_it_keeps_their_mode()
    {
        await using var host = await StartAsync();

        var created = await Run(host.Services, ["setup", "--group", "afisha", "--model-processing", "manual"]);
        var modesAfterCreate = await ModesAsync(host);
        var opened = await Run(host.Services, ["setup", "--group", "afisha", "--model-processing", "AUTO"]);
        var modesAfterOpen = await ModesAsync(host);
        var kept = await Run(host.Services, ["setup", "--group", "afisha", "--schedules"]);
        var modesAfterKeep = await ModesAsync(host);
        var closed = await Run(host.Services, ["setup", "--group", "afisha", "--model-processing", "off"]);
        var modesAfterClose = await ModesAsync(host);

        created.Exit.Should().Be(0, created.Error);
        modesAfterCreate.Should().NotBeEmpty().And.OnlyContain(mode => mode == ModelProcessingMode.Manual);
        opened.Exit.Should().Be(0, opened.Error);
        modesAfterOpen.Should().OnlyContain(mode => mode == ModelProcessingMode.Auto);
        kept.Exit.Should().Be(0, kept.Error);
        modesAfterKeep.Should().OnlyContain(mode => mode == ModelProcessingMode.Auto, "no option leaves an opened source as it is");
        closed.Exit.Should().Be(0, closed.Error);
        modesAfterClose.Should().OnlyContain(mode => mode == ModelProcessingMode.Off);
    }

    [Fact]
    public async Task Setup_without_the_option_registers_closed_and_accepts_a_source_an_operator_opened()
    {
        await using var host = await StartAsync();
        (await Run(host.Services, ["setup", "--group", "afisha"])).Exit.Should().Be(0);
        (await ModesAsync(host)).Should().NotBeEmpty().And.OnlyContain(mode => mode == ModelProcessingMode.Off);
        using (var scope = host.Services.CreateScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
            foreach (var source in await SourcesAsync(registry))
                Assert.IsType<Result<SourceModel>.Success>(await registry.UpdateSourceAsync(source.Id, new() { ModelProcessing = ModelProcessingMode.Auto }));
        }

        var again = await Run(host.Services, ["setup", "--group", "afisha", "--schedules"]);

        again.Exit.Should().Be(0, "a differing mode is no longer a conflict: " + again.Error);
        (await ModesAsync(host)).Should().OnlyContain(mode => mode == ModelProcessingMode.Auto);
    }

    /// <summary>The publishers <c>ocharo-selector.json</c> lists; CAEx's calendar is a selected channel, so the default setup lacks it.</summary>
    private static readonly string[] SelectorGroups = ["afisha", "anons", "caex", "uzexpocentre"];

    [Fact]
    public async Task Setup_WithSelector_ShouldCreateOneSubscription_AndReuseIt()
    {
        await using var host = await StartAsync();

        var first = await Run(host.Services, SelectorSetup());
        var again = await Run(host.Services, SelectorSetup());

        first.Exit.Should().Be(0, first.Error);
        again.Exit.Should().Be(0, again.Error);
        var created = Last(first.Output);
        var reused = Last(again.Output);
        created.GetProperty("selectorVersion").GetInt32().Should().Be(1);
        reused.GetProperty("selectorSubscriptionId").GetGuid().Should().Be(created.GetProperty("selectorSubscriptionId").GetGuid());
        reused.GetProperty("selectorVersion").GetInt32().Should().Be(1);
        first.Error.Should().Contain("selector ocharo-events: subscription " + created.GetProperty("selectorSubscriptionId").GetGuid());

        var stored = Assert.Single(await SelectorSubscriptionsAsync(host));
        stored.Id.Should().Be(created.GetProperty("selectorSubscriptionId").GetGuid());
        stored.Name.Should().Be("ocharo-events");
        stored.Schema.Should().Be("wow2.event.v1");
        stored.State.Should().Be(SubscriptionState.Active);
        stored.Backfill.Should().Be(BackfillMode.Live);
        stored.PipelineId.Should().BeNull();
        stored.FilterKey.Should().BeNull();
        stored.SelectorVersion.Should().Be(1);
        stored.Context.Should().Equal(new Dictionary<string, string> { ["catalog"] = "ocharo-fashion" });
        stored.Selector!.EntityTypes.Should().Equal("event");
        stored.Selector.ExcludeTimeframes.Should().Equal("past");
        var include = stored.Selector.Sources.Include;
        include.Select(reference => reference.Context["sourceGroup"]).Should().Equal(SelectorGroups);
        include.Single(reference => reference.Context["sourceGroup"] == "caex").Context["defaultKind"].Should().Be("exhibition");
        (await GroupNamesAsync(host, include.Select(reference => reference.GroupId!.Value))).Should()
            .Equal("Afisha Uzbekistan", "ANONS Uzbekistan", "CAEx Uzbekistan", "Uzexpocentre");
        reused.GetProperty("subscriptions").GetArrayLength().Should().BePositive("the per-pipeline filter subscriptions keep being created");
    }

    [Fact]
    public async Task Setup_WithoutSelector_ShouldCreateNoSelectorSubscription_AndPrintNoSelectorFields()
    {
        await using var host = await StartAsync();

        var setup = await Run(host.Services, ["setup", "--group", "afisha"]);

        setup.Exit.Should().Be(0, setup.Error);
        Last(setup.Output).TryGetProperty("selectorSubscriptionId", out _).Should().BeFalse();
        Last(setup.Output).TryGetProperty("selectorVersion", out _).Should().BeFalse();
        (await SelectorSubscriptionsAsync(host)).Should().BeEmpty();
    }

    [Fact]
    public async Task Setup_WithSelectorReset_ShouldKeepTheVersion_WhenTheFileIsUnchanged()
    {
        await using var host = await StartAsync();
        (await Run(host.Services, SelectorSetup())).Exit.Should().Be(0);

        var reset = await Run(host.Services, [.. SelectorSetup(), "--selector-reset"]);

        reset.Exit.Should().Be(0, reset.Error);
        Last(reset.Output).GetProperty("selectorVersion").GetInt32().Should().Be(1, "a selector equal to the stored one writes nothing");
        (await SelectorSubscriptionsAsync(host)).Should().ContainSingle().Which.SelectorVersion.Should().Be(1);
    }

    [Fact]
    public async Task Setup_WithSelector_ShouldKeepAClientsEdit_AndOnlyAResetPutsTheFileBack()
    {
        await using var host = await StartAsync();
        var created = Last((await Run(host.Services, SelectorSetup())).Output);
        var id = created.GetProperty("selectorSubscriptionId").GetGuid();
        using (var scope = host.Services.CreateScope())
        {
            var delivery = scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();
            var edited = Assert.IsType<Result<SelectorChangeModel>.Success>(await delivery.SetSelectorAsync(id, new()
            {
                Selector = (await SelectorSubscriptionsAsync(host)).Single().Selector! with { ExcludeTopics = ["marathon"] },
                ChangedBy = "a client",
            })).Value;
            edited.Subscription.SelectorVersion.Should().Be(2);
        }

        var kept = Last((await Run(host.Services, SelectorSetup())).Output);
        kept.GetProperty("selectorVersion").GetInt32().Should().Be(2);
        (await SelectorSubscriptionsAsync(host)).Single().Selector!.ExcludeTopics.Should().Equal("marathon");

        var reset = await Run(host.Services, [.. SelectorSetup(), "--selector-reset"]);
        reset.Exit.Should().Be(0, reset.Error);
        Last(reset.Output).GetProperty("selectorVersion").GetInt32().Should().Be(3);
        Last(reset.Output).GetProperty("selectorSubscriptionId").GetGuid().Should().Be(id);
        (await SelectorSubscriptionsAsync(host)).Single().Selector!.ExcludeTopics.Should().BeEmpty();
    }

    [Fact]
    public async Task Setup_WithSelector_ShouldBindEachProvisionedPublisher_AndSkipTheOnesTheFileListsButSetupSkipped()
    {
        await using var host = await StartAsync();

        var first = await Run(host.Services, ["setup", "--group", "afisha", "--selector"]);

        first.Exit.Should().Be(0, first.Error);
        Last(first.Output).GetProperty("selectorVersion").GetInt32().Should().Be(1);
        var stored = Assert.Single(await SelectorSubscriptionsAsync(host));
        stored.Selector!.Sources.Include.Select(reference => reference.Context["sourceGroup"]).Should().Equal("afisha");

        var more = await Run(host.Services, ["setup", "--group", "caex", "--group", "anons", "--selector", "--selector-reset"]);

        more.Exit.Should().Be(0, more.Error);
        Last(more.Output).GetProperty("selectorVersion").GetInt32().Should().Be(2);
        var include = Assert.Single(await SelectorSubscriptionsAsync(host)).Selector!.Sources.Include;
        include.Select(reference => reference.Context["sourceGroup"]).Should().Equal("afisha", "anons", "caex");
        include.Single(reference => reference.Context["sourceGroup"] == "caex").Context["defaultKind"].Should().Be("exhibition");
    }

    private static string[] SelectorSetup() => ["setup", .. SelectorGroups.SelectMany(group => new[] { "--group", group }), "--selector"];

    private static JsonElement Last(string output) => JsonDocument.Parse(output.TrimEnd().Split('\n')[^1]).RootElement;

    private static async Task<List<SubscriptionModel>> SelectorSubscriptionsAsync(WebApplication host)
    {
        using var scope = host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var tenant = Assert.IsType<Result<IReadOnlyList<TenantModel>>.Success>(await registry.ListTenantsAsync()).Value.Single(item => item.Slug == "ocharo");
        var subscriptions = Assert.IsType<Result<IReadOnlyList<SubscriptionModel>>.Success>(
            await scope.ServiceProvider.GetRequiredService<IDeliveryBroker>().ListSubscriptionsAsync(tenant.Id)).Value;
        return [.. subscriptions.Where(subscription => subscription.Kind == SubscriptionKind.Selector)];
    }

    private static async Task<string[]> GroupNamesAsync(WebApplication host, IEnumerable<Guid> ids)
    {
        using var scope = host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var tenant = Assert.IsType<Result<IReadOnlyList<TenantModel>>.Success>(await registry.ListTenantsAsync()).Value.Single(item => item.Slug == "ocharo");
        var container = Assert.IsType<Result<IReadOnlyList<ContainerModel>>.Success>(await registry.ListContainersAsync(tenant.Id)).Value.Single();
        var groups = Assert.IsType<Result<IReadOnlyList<SourceGroupModel>>.Success>(await registry.ListSourceGroupsAsync(container.Id)).Value;
        return [.. ids.Select(id => groups.Single(group => group.Id == id).Name)];
    }

    private static async Task<List<ModelProcessingMode>> ModesAsync(WebApplication host)
    {
        using var scope = host.Services.CreateScope();
        return (await SourcesAsync(scope.ServiceProvider.GetRequiredService<IRegistrationBroker>())).Select(source => source.ModelProcessing).ToList();
    }

    private static async Task<List<SourceModel>> SourcesAsync(IRegistrationBroker registry)
    {
        var tenants = Assert.IsType<Result<IReadOnlyList<TenantModel>>.Success>(await registry.ListTenantsAsync()).Value;
        var sources = new List<SourceModel>();
        foreach (var container in Assert.IsType<Result<IReadOnlyList<ContainerModel>>.Success>(
            await registry.ListContainersAsync(tenants.Single(item => item.Slug == "ocharo").Id)).Value)
            sources.AddRange(Assert.IsType<Result<IReadOnlyList<SourceModel>>.Success>(await registry.ListSourcesAsync(container.Id)).Value);
        return sources;
    }

    [Theory]
    [InlineData("ocharo")]
    [InlineData("ocharo setup --group")]
    [InlineData("ocharo setup --force")]
    [InlineData("ocharo setup --model-processing")]
    [InlineData("ocharo setup --model-processing loud")]
    [InlineData("ocharo setup --model-processing auto --model-processing off")]
    [InlineData("ocharo setup --selector-reset")]
    public async Task Setup_answers_its_usage_without_serving(string commandLine)
    {
        string[] args = commandLine.Split(' ');
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await ServiceHost.RunAsync(args,
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DB_CONNECTION"] = "Host=127.0.0.1;Port=1;Database=unreachable;Username=synthetic;Timeout=1",
                });
                builder.Configure();
            },
            _ => throw new InvalidOperationException("A verb must not start the source host."),
            verbs => verbs.AddHostVerb(OcharoSetupVerb.Name, (services, verb, ct) =>
                OcharoSetupVerb.RunAsync(services, verb.Arguments, verb.Output, verb.Error, ct)),
            output, error, CancellationToken.None);

        exit.Should().Be(2, error.ToString());
        error.ToString().Should().Contain("Usage: ocharo setup");
        output.ToString().Should().BeEmpty();
    }

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
            ["Harvest:Operator:ApiKey"] = "hv_OcharoSetupVerbTestOperator00000",
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

    private static async Task<(int Exit, string Output, string Error)> Run(IServiceProvider services, string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await OcharoSetupVerb.RunAsync(services, arguments, output, error, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }
}
