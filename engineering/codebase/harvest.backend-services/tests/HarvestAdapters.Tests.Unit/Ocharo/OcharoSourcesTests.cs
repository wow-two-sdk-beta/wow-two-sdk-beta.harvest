using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

namespace HarvestAdapters.Tests.Unit.Ocharo;

public sealed class OcharoSourcesTests
{
    [Theory]
    [InlineData("Fashline 2026", "Fashion show in Tashkent", true)]
    [InlineData("Texworld Tashkent 2026", "International exhibition", true)]
    [InlineData("Atlas bayrami", "Marg‘ilon festivali", true)]
    [InlineData("Craft Fair 2026", "Applications are closed", true)]
    [InlineData("Дизайнеры: приём заявок на конкурс", "Узбекистан", true)]
    [InlineData("Ҳунармандлар кўргазмаси", "Самарқанд", true)]
    [InlineData("Silk and textile market", "Bukhara", true)]
    [InlineData("Ярмарка Park in Mall Market", "Изделия ручной работы, украшения, аксессуары", true)]
    [InlineData("HandFest", "Handmade accessory market", true)]
    [InlineData("Показ мод", "Новая коллекция", true)]
    [InlineData("Moda haftaligi", "Toshkent", true)]
    [InlineData("Minecraft conference", "A gaming event", false)]
    [InlineData("Warcraft festival", "A gaming event", false)]
    [InlineData("Craft beer festival", "A food and drink event", false)]
    [InlineData("Concert", "Live music", false)]
    [InlineData("Weekly restaurant market", "Food and drinks", false)]
    [InlineData("New designer collection", "New clothes in the shop", false)]
    public void Filter_requires_subject_and_opportunity(string title, string text, bool expected)
    {
        new OcharoFashionFilter().Matches(Envelope(title, text)).Should().Be(expected);
    }

    [Fact]
    public void Editorial_fashion_reports_do_not_become_event_candidates()
    {
        var envelope = Envelope("Fashion Week report", "Fashion show") with
        {
            Extras = new Dictionary<string, JsonElement>
            {
                ["schemaOrg"] = JsonSerializer.SerializeToElement(new { afisha = new { recordKind = "article" } }),
            },
        };
        new OcharoFashionFilter().Matches(envelope).Should().BeFalse();
        new OcharoFashionFilter().Matches(Envelope("Fashion Week", "Fashion show") with { Kind = PostKind.News }).Should().BeFalse();
    }

    [Fact]
    public async Task Setup_is_repeatable_private_filtered_and_manual_by_default()
    {
        var registry = new FakeRegistrationBroker();
        var posts = new FakePostBroker(registry);
        var delivery = new FakeDeliveryBroker(posts, registry, null, [new OcharoFashionFilter()]);
        var provisioner = new OcharoSourceProvisioner(registry, delivery);
        OcharoSetup first = Value(await provisioner.EnsureAsync());
        OcharoSetup second = Value(await provisioner.EnsureAsync());
        second.Should().BeEquivalentTo(first);
        first.Sources.Should().HaveCount(61, "the five default named channels, the 25 Wave 1 generic channels, the 10 Wave 2 Telegram channels and the 21 Wave 5 channels");
        first.Sources.Select(item => item.SourceId).Distinct().Should().HaveCount(60, "Afisha's two channels share a source");
        first.Sources[0].SourceKey.Should().Be("afisha-uz");
        var sources = Value(await registry.ListSourcesAsync(first.ContainerId));
        sources.Should().OnlyContain(source => !source.AiAllowed && !source.PoolShareable
            && source.RobotsPolicy == RobotsPolicy.Obey && source.Completeness == SourceCompleteness.Window);
        sources.Single(source => source.ExternalRef == "uzexpocentre-uz").CheckPolicy.Should().Be("none");
        var pipelines = Value(await registry.ListPipelinesAsync(first.ContainerId));
        pipelines.Should().OnlyContain(pipeline => pipeline.Ai == AiMode.Off && pipeline.Schedules.All(schedule => !schedule.Enabled));
        var subscriptions = Value(await delivery.ListSubscriptionsAsync(first.TenantId));
        subscriptions.Should().HaveCount(61).And.OnlyContain(item => item.FilterKey == OcharoFashionFilter.FilterKey
            && !item.Context.ContainsKey("review"));
        registry.IssuedSecrets.Should().BeEmpty();
    }

    [Fact]
    public async Task Setup_can_explicitly_enable_new_schedules()
    {
        var registry = new FakeRegistrationBroker();
        var delivery = new FakeDeliveryBroker(new FakePostBroker(registry), registry, null, [new OcharoFashionFilter()]);
        OcharoSetup setup = Value(await new OcharoSourceProvisioner(registry, delivery).EnsureAsync(enableSchedules: true));
        var pipelines = Value(await registry.ListPipelinesAsync(setup.ContainerId));
        pipelines.Should().OnlyContain(pipeline => pipeline.Schedules.All(schedule => schedule.Enabled));
    }

    [Fact]
    public async Task Setup_declares_the_capture_on_the_profiles_cron_and_the_check_on_a_day()
    {
        var registry = new FakeRegistrationBroker();
        var delivery = new FakeDeliveryBroker(new FakePostBroker(registry), registry, null, [new OcharoFashionFilter()]);
        var provisioner = new OcharoSourceProvisioner(registry, delivery);

        OcharoSetup manual = Value(await provisioner.EnsureAsync(groupIds: ["afisha"]));
        OcharoSetup scheduled = Value(await provisioner.EnsureAsync(enableSchedules: true, includeInstagram: true, groupIds: ["afisha", "teplo"]));

        foreach (var route in manual.Sources)
        {
            var pipeline = Value(await registry.GetPipelineAsync(route.PipelineId));
            var capture = pipeline.Schedules.Single(row => row.Kind == WorkKind.Capture);
            (capture.Mode, capture.Cron, capture.Every, capture.TimeZone, capture.Enabled)
                .Should().Be((ScheduleMode.Cron, "0 1,7,13,19 * * *", (TimeSpan?)null, "Asia/Tashkent", true), "the request switched the declared row on");
            var check = pipeline.Schedules.Single(row => row.Kind == WorkKind.Check);
            (check.Mode, check.Cron, check.Every, check.TimeZone, check.Enabled)
                .Should().Be((ScheduleMode.Interval, (string?)null, (TimeSpan?)TimeSpan.FromHours(24), "Asia/Tashkent", true));
        }
        foreach (var route in scheduled.Sources.Where(route => route.GroupId == "teplo"))
        {
            var capture = Value(await registry.GetPipelineAsync(route.PipelineId)).Schedules.Should().ContainSingle().Subject;
            (capture.Kind, capture.Mode, capture.Cron, capture.Enabled)
                .Should().Be((WorkKind.Capture, ScheduleMode.Cron, "0 1,7,13,19 * * *", false), "Instagram capture stays disabled");
        }
    }

    [Fact]
    public async Task Setup_with_schedules_keeps_the_fixed_six_hours_an_earlier_setup_declared_and_adds_the_cron_where_there_is_no_capture()
    {
        var registry = new FakeRegistrationBroker();
        var delivery = new FakeDeliveryBroker(new FakePostBroker(registry), registry, null, [new OcharoFashionFilter()]);
        var provisioner = new OcharoSourceProvisioner(registry, delivery);
        OcharoSetup earlier = Value(await provisioner.EnsureAsync(groupIds: ["afisha"]));
        var (sixHours, bare) = (earlier.Sources[0].PipelineId, earlier.Sources[1].PipelineId);
        foreach (var (id, schedules) in new (Guid, List<ScheduleDeclaration>)[]
        {
            (sixHours, [new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(6), TimeZone = "Asia/Tashkent", Enabled = false }]),
            (bare, []),
        })
        {
            var pipeline = Value(await registry.GetPipelineAsync(id));
            Value(await registry.UpdatePipelineAsync(id, new() { Name = pipeline.Name, Scope = pipeline.Scope, Schedules = schedules }));
        }

        Value(await provisioner.EnsureAsync(enableSchedules: true, groupIds: ["afisha"])).Should().BeEquivalentTo(earlier);

        var kept = Value(await registry.GetPipelineAsync(sixHours)).Schedules.Single(row => row.Kind == WorkKind.Capture);
        (kept.Mode, kept.Every, kept.Enabled).Should().Be((ScheduleMode.Interval, (TimeSpan?)TimeSpan.FromHours(6), true));
        var added = Value(await registry.GetPipelineAsync(bare)).Schedules.Single(row => row.Kind == WorkKind.Capture);
        (added.Mode, added.Cron, added.Enabled).Should().Be((ScheduleMode.Cron, "0 1,7,13,19 * * *", true));
    }

    [Fact]
    public async Task Setup_with_schedules_switches_on_existing_pipelines_and_keeps_a_tuned_cadence()
    {
        var registry = new FakeRegistrationBroker();
        var delivery = new FakeDeliveryBroker(new FakePostBroker(registry), registry, null, [new OcharoFashionFilter()]);
        var provisioner = new OcharoSourceProvisioner(registry, delivery);
        OcharoSetup manual = Value(await provisioner.EnsureAsync(groupIds: ["afisha"]));
        PipelineModel tuned = Value(await registry.GetPipelineAsync(manual.Sources[0].PipelineId));
        Value(await registry.UpdatePipelineAsync(tuned.Id, new()
        {
            Name = tuned.Name, Scope = tuned.Scope,
            Schedules = tuned.Schedules.Select(row => new ScheduleDeclaration
            {
                Kind = row.Kind, Mode = row.Kind == WorkKind.Capture ? ScheduleMode.Interval : row.Mode,
                Every = row.Kind == WorkKind.Capture ? TimeSpan.FromHours(12) : row.Every,
                TimeZone = row.TimeZone, Enabled = false,
            }).ToList(),
        }));

        OcharoSetup scheduled = Value(await provisioner.EnsureAsync(enableSchedules: true, groupIds: ["afisha"]));

        scheduled.Should().BeEquivalentTo(manual);
        var pipelines = Value(await registry.ListPipelinesAsync(manual.ContainerId));
        pipelines.Should().OnlyContain(pipeline => pipeline.Schedules.Count > 0 && pipeline.Schedules.All(schedule => schedule.Enabled));
        Value(await registry.GetPipelineAsync(tuned.Id)).Schedules.Single(row => row.Kind == WorkKind.Capture).Every
            .Should().Be(TimeSpan.FromHours(12));
        Value(await provisioner.EnsureAsync(enableSchedules: true, groupIds: ["afisha"])).Should().BeEquivalentTo(manual);
    }

    [Fact]
    public async Task Setup_names_each_source_and_groups_it_under_its_publisher_keeping_an_operators_choice()
    {
        var registry = new FakeRegistrationBroker();
        var delivery = new FakeDeliveryBroker(new FakePostBroker(registry), registry, null, [new OcharoFashionFilter()]);
        var provisioner = new OcharoSourceProvisioner(registry, delivery);
        OcharoSetup setup = Value(await provisioner.EnsureAsync(groupIds: ["afisha"]));

        var source = Value(await registry.GetSourceAsync(setup.Sources[0].SourceId));
        var groups = Value(await registry.ListSourceGroupsAsync(setup.ContainerId));
        groups.Should().ContainSingle().Which.Name.Should().Be(OcharoSourceRegistry.Current.Groups.Single(group => group.Id == "afisha").Name);
        source.GroupId.Should().Be(groups[0].Id);
        source.Name.Should().Be("afisha.uz");

        Value(await registry.UpdateSourceAsync(source.Id, new() { Name = "Afisha site" }));
        Value(await provisioner.EnsureAsync(groupIds: ["afisha"]));
        Value(await registry.GetSourceAsync(source.Id)).Name.Should().Be("Afisha site");
        Value(await registry.ListSourceGroupsAsync(setup.ContainerId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Setup_takes_the_requested_model_processing_for_new_and_existing_sources_and_otherwise_keeps_it()
    {
        var registry = new FakeRegistrationBroker();
        var delivery = new FakeDeliveryBroker(new FakePostBroker(registry), registry, null, [new OcharoFashionFilter()]);
        var provisioner = new OcharoSourceProvisioner(registry, delivery);

        OcharoSetup manual = Value(await provisioner.EnsureAsync(groupIds: ["afisha"], modelProcessing: ModelProcessingMode.Manual));
        Value(await registry.ListSourcesAsync(manual.ContainerId)).Should().ContainSingle().Which.ModelProcessing.Should().Be(ModelProcessingMode.Manual);

        Value(await provisioner.EnsureAsync(groupIds: ["afisha"], modelProcessing: ModelProcessingMode.Auto)).Should().BeEquivalentTo(manual);
        Value(await registry.ListSourcesAsync(manual.ContainerId)).Single().ModelProcessing.Should().Be(ModelProcessingMode.Auto);

        Value(await provisioner.EnsureAsync(enableSchedules: true, groupIds: ["afisha"])).Should().BeEquivalentTo(manual);
        Value(await registry.ListSourcesAsync(manual.ContainerId)).Single().ModelProcessing.Should().Be(ModelProcessingMode.Auto);

        Value(await provisioner.EnsureAsync(groupIds: ["afisha"], modelProcessing: ModelProcessingMode.Off)).Should().BeEquivalentTo(manual);
        Value(await registry.ListSourcesAsync(manual.ContainerId)).Single().ModelProcessing.Should().Be(ModelProcessingMode.Off);
    }

    private static PostEnvelope Envelope(string title, string text) => new()
    {
        Kind = PostKind.Event, Title = title, Text = text,
        Extraction = new() { NormVersion = "test@1", RulesVersion = "test@1" },
    };
    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success
        ? success.Value : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
}
