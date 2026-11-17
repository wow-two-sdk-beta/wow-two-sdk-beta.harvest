using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

namespace HarvestAdapters.Tests.Unit.Ocharo;

public sealed class OcharoRegistryTests
{
    [Fact]
    public void Group_roles_and_event_families_are_independent_of_host_and_rank()
    {
        var registry = OcharoSourceRegistry.Current;
        registry.Groups.Should().HaveCount(100);
        var teplo = registry.Groups.Single(group => group.Id == "teplo");
        teplo.Channels.Single(channel => channel.Primary).Mode.Should().Be("reference");
        teplo.Channels.Where(channel => channel.Mode == "provider").Select(channel => channel.EventFamily)
            .Should().BeEquivalentTo(new string?[] { "teplo-market", "teplo-fest", null });
        var store = teplo.Channels.Single(channel => channel.Id == "store-pop-ups-and-reposts");
        store.Mode.Should().Be("provider");
        store.DisplayName.Should().Be("Teplo Store");
        store.EventFamily.Should().BeNull();

        var idem = registry.Groups.Single(group => group.Id == "idem");
        idem.Aggregates.Should().Contain(["caex", "iticket"]);
        var venue = registry.Groups.Single(group => group.Id == "caex");
        venue.Channels.Single(channel => channel.Primary).AdapterKey.Should().Be("caex-uz");
        var visa = registry.Groups.Single(group => group.Id == "vfw-tashkent");
        visa.Channels.Single(channel => channel.RelatedGroupId == "uzexpocentre")
            .Mode.Should().Be("reference");
    }

    [Theory]
    [InlineData("iteca-uz")]
    [InlineData("teplo")]
    [InlineData("unknown")]
    public async Task Invalid_or_unsupported_selection_fails_before_writing(string groupId)
    {
        var registry = new FakeRegistrationBroker();
        var provisioner = new OcharoSourceProvisioner(registry, Delivery(registry));
        (await provisioner.EnsureAsync(groupIds: ["afisha", groupId])).Should().BeOfType<Result<OcharoSetup>.Failure>();
        Value(await registry.ListTenantsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Publisher_selection_provisions_distinct_named_teplo_sources_without_enabling_schedules()
    {
        var registry = new FakeRegistrationBroker();
        var delivery = Delivery(registry);
        var provisioner = new OcharoSourceProvisioner(registry, delivery);

        var setup = Value(await provisioner.EnsureAsync(enableSchedules: true, includeInstagram: true, groupIds: ["teplo"]));
        var repeat = Value(await provisioner.EnsureAsync(includeInstagram: true, groupIds: ["teplo"]));

        repeat.Should().BeEquivalentTo(setup);
        setup.Sources.Should().HaveCount(3).And.OnlyContain(route => route.GroupId == "teplo");
        setup.Sources.Select(route => route.SourceId).Distinct().Should().HaveCount(3);
        setup.Sources.Select(route => route.PipelineId).Distinct().Should().HaveCount(3);
        setup.Sources.Select(route => route.SubscriptionId).Distinct().Should().HaveCount(3);
        var sources = Value(await registry.ListSourcesAsync(setup.ContainerId));
        sources.Select(source => (source.Handle, source.Name)).Should().BeEquivalentTo(new (string Handle, string? Name)[]
        {
            ("teplomarket_", "Teplo Market"), ("teplofest", "Teplo Fest"), ("teplostore_2022", "Teplo Store"),
        });
        var group = Value(await registry.ListSourceGroupsAsync(setup.ContainerId)).Should().ContainSingle().Subject;
        group.Name.Should().Be("Teplo Store");
        sources.Should().OnlyContain(source => source.GroupId == group.Id);
        Value(await registry.ListPipelinesAsync(setup.ContainerId)).Should()
            .OnlyContain(pipeline => pipeline.Schedules.All(schedule => !schedule.Enabled));
        var subscriptions = Value(await delivery.ListSubscriptionsAsync(setup.TenantId));
        foreach (var route in setup.Sources)
        {
            var context = subscriptions.Single(subscription => subscription.Id == route.SubscriptionId).Context;
            context["sourceGroup"].Should().Be(route.GroupId);
            context["sourceChannel"].Should().Be(route.ChannelId);
            if (route.EventFamily is { } family) context["eventFamily"].Should().Be(family);
            else context.Should().NotContainKey("eventFamily");
        }
    }

    [Fact]
    public async Task Publisher_status_does_not_bind_to_a_legacy_domain_pipeline_with_the_same_profile_url()
    {
        var registry = new FakeRegistrationBroker();
        var provisioner = new OcharoSourceProvisioner(registry, Delivery(registry));
        var initial = Value(await provisioner.EnsureAsync(groupIds: ["afisha"]));
        var legacy = Value(await registry.RegisterSourceAsync(new()
        {
            ContainerId = initial.ContainerId, Family = SourceFamily.Instagram, Handle = "www.instagram.com",
            Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window, Posture = SourcePosture.Official,
        }));
        var legacyPipeline = Value(await registry.DeclarePipelineAsync(new()
        {
            ContainerId = initial.ContainerId, SourceId = legacy.Id,
            Scope = new() { Url = "https://www.instagram.com/teplofest/" },
        }));
        Value(await provisioner.ReadAsync(groupIds: ["teplo"])).Channels.Single(channel => channel.ChannelId == "fest")
            .Pipeline.Should().BeNull();

        var setup = Value(await provisioner.EnsureAsync(includeInstagram: true, groupIds: ["teplo"]));
        var status = Value(await provisioner.ReadAsync(groupIds: ["teplo"]));
        var fest = status.Channels.Single(channel => channel.ChannelId == "fest").Pipeline!;

        fest.Id.Should().Be(setup.Sources.Single(channel => channel.ChannelId == "fest").PipelineId);
        fest.Id.Should().NotBe(legacyPipeline.Id);
        Value(await registry.GetPipelineAsync(legacyPipeline.Id)).Should().BeEquivalentTo(legacyPipeline);
    }

    [Fact]
    public async Task Selected_calendar_uses_a_window_capture_and_never_probes_event_detail_links()
    {
        var registry = new FakeRegistrationBroker();
        var provisioner = new OcharoSourceProvisioner(registry, Delivery(registry));
        var setup = Value(await provisioner.EnsureAsync(groupIds: ["caex"]));
        var route = setup.Sources.Should().ContainSingle().Subject;
        route.GroupId.Should().Be("caex");
        route.ChannelId.Should().Be("calendar");
        route.Seed.Should().Be("https://caexuzbekistan.com/events-calendar/");
        var source = Value(await registry.ListSourcesAsync(setup.ContainerId)).Should().ContainSingle().Subject;
        source.Completeness.Should().Be(SourceCompleteness.Window);
        source.CheckPolicy.Should().Be("none");
        source.RobotsPolicy.Should().Be(RobotsPolicy.Obey);
        var pipeline = Value(await registry.ListPipelinesAsync(setup.ContainerId)).Should().ContainSingle().Subject;
        var schedule = pipeline.Schedules.Should().ContainSingle().Subject;
        schedule.Kind.Should().Be(WorkKind.Capture);
        schedule.Enabled.Should().BeFalse();
    }

    private static FakeDeliveryBroker Delivery(FakeRegistrationBroker registry) =>
        new(new FakePostBroker(registry), registry, null, [new OcharoFashionFilter()]);

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success
        ? success.Value : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
}
