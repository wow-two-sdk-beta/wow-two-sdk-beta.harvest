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
            .Should().BeEquivalentTo("teplo-market", "teplo-fest");
        teplo.Channels.Single(channel => channel.Id == "store-pop-ups-and-reposts").Mode.Should().Be("manual");

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
    public async Task Publisher_selection_provisions_two_distinct_teplo_channels_with_one_transport_source()
    {
        var registry = new FakeRegistrationBroker();
        var delivery = Delivery(registry);
        var provisioner = new OcharoSourceProvisioner(registry, delivery);

        var setup = Value(await provisioner.EnsureAsync(enableSchedules: true, includeInstagram: true, groupIds: ["teplo"]));
        var repeat = Value(await provisioner.EnsureAsync(includeInstagram: true, groupIds: ["teplo"]));

        repeat.Should().BeEquivalentTo(setup);
        setup.Sources.Should().HaveCount(2).And.OnlyContain(route => route.GroupId == "teplo");
        setup.Sources.Select(route => route.SourceId).Distinct().Should().ContainSingle();
        setup.Sources.Select(route => route.PipelineId).Distinct().Should().HaveCount(2);
        setup.Sources.Select(route => route.SubscriptionId).Distinct().Should().HaveCount(2);
        Value(await registry.ListPipelinesAsync(setup.ContainerId)).Should()
            .OnlyContain(pipeline => pipeline.Schedules.All(schedule => !schedule.Enabled));
        var subscriptions = Value(await delivery.ListSubscriptionsAsync(setup.TenantId));
        foreach (var route in setup.Sources)
        {
            var context = subscriptions.Single(subscription => subscription.Id == route.SubscriptionId).Context;
            context["sourceGroup"].Should().Be(route.GroupId);
            context["sourceChannel"].Should().Be(route.ChannelId);
            context["eventFamily"].Should().Be(route.EventFamily);
        }
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
