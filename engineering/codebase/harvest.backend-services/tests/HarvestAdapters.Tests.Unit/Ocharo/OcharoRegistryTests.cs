using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Telegram;

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

    [Fact]
    public void The_shipped_registry_still_validates()
    {
        var registry = OcharoSourceRegistry.Current;

        registry.Validate();

        registry.Groups.Should().HaveCount(100);
        registry.Groups.SelectMany(group => group.Channels).Count(channel => channel.Mode == "harvest")
            .Should().Be(41, "the 6 named readers' channels, the 25 Wave 1 generic channels and the 10 Wave 2 Telegram channels");
    }

    [Theory]
    [InlineData("generic-feed", SourceFamily.Feed, "none", "https://news.example.test/feed.xml")]
    [InlineData("generic-structured", SourceFamily.Structured, "web-direct", "https://events.example.test/calendar")]
    [InlineData("generic-site", SourceFamily.Site, "web-direct", "https://venue.example.test/")]
    public void A_generic_route_is_listed_with_its_family_and_the_cron_capture_cadence(string key, SourceFamily family,
        string checkPolicy, string url)
    {
        var registry = Registry(Channel("news", key, url));

        registry.Validate();
        var route = registry.Routes(includeInstagram: false).Should().ContainSingle().Subject;

        route.Profile.Key.Should().Be(key);
        route.Profile.Family.Should().Be(family);
        route.Profile.Authority.Should().Be(60);
        route.Profile.CheckPolicy.Should().Be(checkPolicy);
        route.Profile.MaxPages.Should().Be(5);
        route.Channel.PipelineUrl.Should().Be(url);
        route.Profile.Cadence.Should().Be(OcharoSourceRegistry.CaptureCadence.Default);
        route.Profile.Cadence.Declare(enabled: true).Should().BeEquivalentTo(new ScheduleDeclaration
        {
            Kind = WorkKind.Capture, Mode = ScheduleMode.Cron, Cron = "0 1,7,13,19 * * *", TimeZone = "Asia/Tashkent", Enabled = true,
        });
        route.Profile.Cadence.Declare(enabled: false).Enabled.Should().BeFalse();
    }

    [Fact]
    public void Every_profile_takes_the_default_capture_cadence_named_ones_included()
    {
        var registry = OcharoSourceRegistry.Current;
        var routes = registry.Routes(includeInstagram: true, registry.Groups.Select(group => group.Id).ToHashSet()).ToArray();

        routes.Select(route => route.Profile.Key).Distinct().Should().BeEquivalentTo(
            "afisha-uz", "anons-uz", "craftfair-uz", "uzexpocentre-uz", "caex-uz", "generic-feed", "generic-structured",
            "generic-site", "instagram-business-discovery", "telegram-preview");
        routes.Should().OnlyContain(route => route.Profile.Cadence == OcharoSourceRegistry.CaptureCadence.Default);
        var cadence = OcharoSourceRegistry.CaptureCadence.Default;
        (cadence.Mode, cadence.Cron, cadence.Every, cadence.TimeZone).Should().Be((ScheduleMode.Cron, "0 1,7,13,19 * * *", (TimeSpan?)null, "Asia/Tashkent"));
    }

    [Fact]
    public void A_generic_channel_keeps_the_harvest_rules()
    {
        const string feed = "https://news.example.test/feed.xml";
        var invalid = new Dictionary<string, Func<OcharoSourceRegistry>>
        {
            ["not owned"] = () => Registry(Channel("news", "generic-feed", feed, relationship: "related")),
            ["repeated seed"] = () => Registry(Channel("a", "generic-feed", feed), Channel("b", "generic-site", feed)),
            ["not https"] = () => Registry(Channel("news", "generic-feed", "http://news.example.test/feed.xml")),
            ["no such route"] = () => Registry(Channel("news", "generic-page", "https://news.example.test/")),
            ["no setup"] = () => Registry(Channel("news", "generic-feed", feed, setup: "later")),
        };

        foreach (var (reason, build) in invalid)
            build().Invoking(registry => registry.Validate()).Should().Throw<InvalidOperationException>(reason);
    }

    [Fact]
    public void A_generic_routes_source_reaches_the_generic_adapter_of_its_family_and_a_named_host_keeps_its_reader()
    {
        using var provider = new ServiceCollection().AddHarvestReaders().AddOcharoEventSources().BuildServiceProvider();
        var adapters = provider.GetServices<ISourceAdapter>().ToList();
        // The engine takes the last adapter that serves the source, so a named adapter wins over a generic one.
        ISourceAdapter Serving(SourceFamily family, string handle) => adapters.Last(adapter => adapter.Serves(Source(family, handle)));
        var registry = Registry(
            Channel("feed", "generic-feed", "https://news.example.test/feed.xml"),
            Channel("events", "generic-structured", "https://events.example.test/calendar"),
            Channel("site", "generic-site", "https://venue.example.test/"));

        var routes = registry.Routes(includeInstagram: false).ToArray();

        routes.Should().HaveCount(3);
        foreach (var route in routes)
            Serving(route.Profile.Family, new Uri(route.Channel.PipelineUrl).IdnHost).Key.Should().Be(route.Profile.Key);
        Serving(SourceFamily.Site, "afisha.uz").Key.Should().Be("afisha-uz");
    }

    [Theory]
    [InlineData("ticketon")]
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
    public async Task Unselected_status_lists_a_provisioned_selected_channel_beside_the_defaults()
    {
        var registry = new FakeRegistrationBroker();
        var provisioner = new OcharoSourceProvisioner(registry, Delivery(registry));
        Value(await provisioner.EnsureAsync(groupIds: ["afisha"]));

        Value(await provisioner.ReadAsync()).Channels.Should().NotContain(channel => channel.GroupId == "caex" && channel.ChannelId == "calendar");

        var caex = Value(await provisioner.EnsureAsync(groupIds: ["caex"])).Sources.Single(route => route.ChannelId == "calendar");
        var status = Value(await provisioner.ReadAsync());

        status.Channels.Single(channel => channel.GroupId == "caex" && channel.ChannelId == "calendar").Pipeline!.Id.Should().Be(caex.PipelineId);
        status.Channels.Should().Contain(channel => channel.GroupId == "afisha");
    }

    [Fact]
    public async Task Selected_calendar_uses_a_window_capture_and_never_probes_event_detail_links()
    {
        var registry = new FakeRegistrationBroker();
        var provisioner = new OcharoSourceProvisioner(registry, Delivery(registry));
        var setup = Value(await provisioner.EnsureAsync(groupIds: ["caex"]));
        setup.Sources.Select(item => item.ChannelId).Should().BeEquivalentTo("calendar", "baraka-retail-fair");
        var route = setup.Sources.Single(item => item.ChannelId == "calendar");
        route.GroupId.Should().Be("caex");
        route.Seed.Should().Be("https://caexuzbekistan.com/events-calendar/");
        var source = Value(await registry.ListSourcesAsync(setup.ContainerId)).Single(item => item.ExternalRef == "caex-uz");
        source.Completeness.Should().Be(SourceCompleteness.Window);
        source.CheckPolicy.Should().Be("none");
        source.RobotsPolicy.Should().Be(RobotsPolicy.Obey);
        var pipeline = Value(await registry.ListPipelinesAsync(setup.ContainerId)).Single(item => item.SourceId == route.SourceId);
        var schedule = pipeline.Schedules.Should().ContainSingle().Subject;
        schedule.Kind.Should().Be(WorkKind.Capture);
        schedule.Enabled.Should().BeFalse();
    }

    /// <summary>Wave 1: each registry channel that takes a generic route, the route and its pipeline seed. A feed channel
    /// seeds at its feed, a structured one at its page and a site one at the sitemap its robots.txt names, so the reader
    /// walks articles. Seeds read 2026-11-21; the Wave 1 rows are in
    /// <c>engineering/research/ocharo-source-registry.md</c>.</summary>
    private static readonly (string Channel, string Route, string Seed)[] Wave1 =
    [
        ("caex/baraka-retail-fair", "generic-feed", "https://caex-barakafriday.com/feed/"),
        ("human-house/primary", "generic-feed", "https://human-house.com/feed/"),
        ("idem/festival-listings", "generic-feed", "https://idem.uz/cal/c/festival.ics"),
        ("hd-magazine/primary", "generic-feed", "https://mag.humodoc.com/ru/feed.xml"),
        ("kultura-uz/primary", "generic-feed", "https://www.kultura.uz/rss.cgi?lan=r"),
        ("spot/primary", "generic-feed", "https://www.spot.uz/ru/rss/"),
        ("expo-position/primary", "generic-feed", "https://expoposition.com/feed/"),
        ("atakent/primary", "generic-structured", "https://www.atakentexpo.kz/calendar"),
        ("hunarmand/association-calls", "generic-site", "https://www.hunarmandlar.uz/sitemap.xml"),
        ("bmca-uz/primary", "generic-site", "https://texworld-apparel.uz/sitemap.xml"),
        ("bmca-uz/portfolio", "generic-site", "https://bmca.uz/sitemap.xml"),
        ("bmca-uz/apparel-sourcing", "generic-site", "https://apparel-sourcing.uz/sitemap.xml"),
        ("catexpo/primary", "generic-site", "https://fashionexpo.kz/Sitemap.xml"),
        ("idem/primary", "generic-site", "https://idem.uz/sitemap.xml"),
        ("ttysi/primary", "generic-site", "https://ttysi.uz/sitemap.xml"),
        ("mrdi/primary", "generic-site", "https://mrdi.uz/sitemap.xml"),
        ("sdtu/primary", "generic-site", "https://sdtu.uz/sitemap.xml"),
        ("visituzbek/primary", "generic-site", "https://www.visituzbek.uz/sitemap.xml"),
        ("eventflow/primary", "generic-site", "https://eventflow.uz/sitemap.xml"),
        ("biexpo/primary", "generic-site", "https://biexpo.kg/sitemap.xml"),
        ("central-asia-textile-expo/primary", "generic-site", "https://centralasiatextileexpo.com/sitemap.xml"),
        ("olbilet/primary", "generic-site", "https://olbilet.uz/sitemap.xml"),
        ("aral-school/primary", "generic-site", "https://www.aralschool.uz/sitemap.xml"),
        ("ichkari/primary", "generic-site", "https://ichkari.com/sitemap.xml"),
        ("emkp/primary", "generic-site", "https://www.emkp.org/sitemap.xml"),
    ];

    private static IEnumerable<OcharoSourceRegistry.CollectionRoute> GenericRoutes() => OcharoSourceRegistry.Current
        .Routes(includeInstagram: false).Where(route => route.Profile.Key.StartsWith("generic-", StringComparison.Ordinal));

    [Fact]
    public void Wave_1_routes_each_listed_channel_to_its_generic_profile_on_the_cron_capture_cadence()
    {
        var routes = GenericRoutes().ToArray();

        routes.Select(route => $"{route.Group.Id}/{route.Channel.Id} {route.Profile.Key} {route.Channel.PipelineUrl}")
            .Should().BeEquivalentTo(Wave1.Select(row => $"{row.Channel} {row.Route} {row.Seed}"));
        routes.Should().OnlyContain(route => route.Channel.Mode == "harvest" && route.Channel.Setup == "default"
            && route.Channel.Relationship == "owned" && (route.Channel.Note ?? string.Empty).Contains("2026-11-21")
            && route.Profile.Authority == 60 && route.Profile.Cadence == OcharoSourceRegistry.CaptureCadence.Default);
        foreach (var route in routes)
            route.Profile.Family.Should().Be(route.Profile.Key switch
            {
                "generic-feed" => SourceFamily.Feed, "generic-structured" => SourceFamily.Structured, _ => SourceFamily.Site,
            });
    }

    [Fact]
    public void Wave_1_counts_per_route_are_the_feeds_structured_pages_and_sites_the_probes_found()
    {
        GenericRoutes().GroupBy(route => route.Profile.Key).ToDictionary(group => group.Key, group => group.Count())
            .Should().BeEquivalentTo(new Dictionary<string, int> { ["generic-feed"] = 7, ["generic-structured"] = 1, ["generic-site"] = 17 });
        Wave1.Should().HaveCount(25);
    }

    [Fact]
    public void Wave_1_seeds_are_unique_and_https_and_follow_the_route_family_rule()
    {
        var routes = GenericRoutes().ToArray();

        routes.Select(route => route.Channel.PipelineUrl).Should().OnlyHaveUniqueItems()
            .And.OnlyContain(seed => seed.StartsWith("https://", StringComparison.Ordinal));
        foreach (var route in routes)
        {
            var channel = route.Channel;
            string where = $"{route.Group.Id}/{channel.Id}";
            switch (route.Profile.Family)
            {
                case SourceFamily.Feed:
                    channel.Seed.Should().NotBeNull(where + " seeds at its feed, not at the page that links it");
                    break;
                case SourceFamily.Structured:
                    channel.Seed.Should().BeNull(where + " seeds at its page");
                    break;
                default:
                    // A page seed reads one page and follows no links; a sitemap on the channel's own host walks articles.
                    channel.Seed.Should().NotBeNull(where + " seeds at a sitemap");
                    new Uri(channel.PipelineUrl).AbsolutePath.ToLowerInvariant().Should().EndWith("sitemap.xml", where);
                    new Uri(channel.PipelineUrl).Host.Replace("www.", string.Empty).Should()
                        .Be(new Uri(channel.Url).Host.Replace("www.", string.Empty), where + " reads its own host's sitemap");
                    break;
            }
        }
    }

    [Fact]
    public void Wave_1_leaves_out_the_gov_uz_channels_Ticketon_Afishas_calendar_the_newsrooms_the_related_channels_and_a_shop()
    {
        string[] newsrooms = ["uzdaily", "kabar", "yuz", "dunyo", "namangan-today", "tashkent-city"];
        // Glamour Avenue's robots.txt names a sitemap on another host, a shop's. The related channels are not owned, so none
        // passes the registry check for a harvest channel.
        string[] related = ["acdf/bukhara-biennial-programme", "namangan-region/flower-festival-programme", "kfw/astana-edition-publisher"];
        var channels = OcharoSourceRegistry.Current.Groups
            .SelectMany(group => group.Channels.Select(channel => (Group: group.Id, Channel: channel))).ToArray();

        var leftOut = channels.Where(row => new Uri(row.Channel.Url).Host is "gov.uz" or "www.gov.uz" || row.Group == "ticketon"
            || newsrooms.Contains(row.Group) || row.Group == "afisha" && row.Channel.Id == "calendar"
            || related.Contains($"{row.Group}/{row.Channel.Id}") || row.Group == "glamour-avenue" && row.Channel.Id == "primary").ToArray();

        leftOut.Should().HaveCountGreaterThan(related.Length + newsrooms.Length);
        // Wave 2 takes the one Telegram channel of the tashkent-city newsroom group; the rest of the group stays out.
        leftOut.Where(row => row.Channel.AdapterKey != TelegramSourceAdapter.AdapterKey).Should()
            .OnlyContain(row => row.Channel.Mode != "harvest" && row.Channel.AdapterKey == null && row.Channel.Setup == null);
        channels.Where(row => related.Contains($"{row.Group}/{row.Channel.Id}")).Should()
            .HaveCount(related.Length).And.OnlyContain(row => row.Channel.Relationship == "related");
    }

    [Fact]
    public async Task Default_setup_provisions_every_Wave_1_channel_as_its_own_source_with_the_capture_cron_and_the_profiles_check()
    {
        var registry = new FakeRegistrationBroker();
        var provisioner = new OcharoSourceProvisioner(registry, Delivery(registry));

        var setup = Value(await provisioner.EnsureAsync(enableSchedules: true));

        var wave = setup.Sources.Where(route => route.SourceKey.StartsWith("generic-", StringComparison.Ordinal)).ToArray();
        wave.Select(route => $"{route.GroupId}/{route.ChannelId} {route.SourceKey} {route.Seed}")
            .Should().BeEquivalentTo(Wave1.Select(row => $"{row.Channel} {row.Route} {row.Seed}"));
        wave.Select(route => route.SourceId).Should().OnlyHaveUniqueItems();
        foreach (var route in wave)
        {
            var pipeline = Value(await registry.GetPipelineAsync(route.PipelineId));
            var capture = pipeline.Schedules.Single(row => row.Kind == WorkKind.Capture);
            (capture.Mode, capture.Cron, capture.TimeZone, capture.Enabled)
                .Should().Be((ScheduleMode.Cron, "0 1,7,13,19 * * *", "Asia/Tashkent", true));
            pipeline.Schedules.Count(row => row.Kind == WorkKind.Check).Should().Be(route.SourceKey == "generic-feed" ? 0 : 1);
        }
    }

    /// <summary>Wave 2: each owned registry channel the 2026-11-21 probes routed to the Telegram preview reader, and its username.
    /// A seed is the preview page of the same channel, whatever page the registry cites.</summary>
    private static readonly (string Channel, string Username)[] Wave2 =
    [
        ("uz-tourism/committee-telegram", "uzbektourismofficial"),
        ("iteca-uz/organizer-telegram", "iteca"),
        ("arts-academy/academy-telegram", "artacademyuz"),
        ("qashqadaryo-region/primary", "kashkadaryo_travel"),
        ("navoi-tourism/primary", "navoitourism"),
        ("tashkent-city/primary", "visittashkent"),
        ("goethe-uz/primary", "goetheinstituttaschkent"),
        ("twig/primary", "twig2018"),
        ("biexpo/organizer-telegram", "biexpo4you"),
        ("glamour-avenue/official-telegram", "glamour_avenue"),
    ];

    /// <summary>The probes' other four Telegram routes: channels a publisher does not own, which the registry check keeps out of harvest.</summary>
    private static readonly string[] Wave2Related =
    [
        "fergana-region/regional-tourism-updates", "khorezm-region/regional-tourism-updates",
        "bukhara-region/regional-tourism-updates", "namangan-region/regional-tourism-updates",
    ];

    private static IEnumerable<OcharoSourceRegistry.CollectionRoute> TelegramRoutes() => OcharoSourceRegistry.Current
        .Routes(includeInstagram: false).Where(route => route.Profile.Key == TelegramSourceAdapter.AdapterKey);

    [Fact]
    public void Wave_2_routes_each_owned_telegram_channel_to_the_preview_reader_on_its_public_preview_page()
    {
        var routes = TelegramRoutes().ToArray();

        routes.Select(route => $"{route.Group.Id}/{route.Channel.Id} {route.Channel.PipelineUrl}").Should()
            .BeEquivalentTo(Wave2.Select(row => $"{row.Channel} https://t.me/s/{row.Username}"));
        routes.Should().OnlyContain(route => route.Channel.Mode == "harvest" && route.Channel.Method == "harvest"
            && route.Channel.Setup == "default" && route.Channel.Relationship == "owned"
            && (route.Channel.Note ?? string.Empty).Contains("Wave 2, reviewed 2026-11-21: telegram-preview")
            && route.Profile.Family == SourceFamily.Telegram && route.Profile.Authority == 80 && route.Profile.CheckPolicy == "none"
            && route.Profile.MaxPages == 3 && route.Profile.Cadence == OcharoSourceRegistry.CaptureCadence.Default);
        routes.Select(route => route.Channel.PipelineUrl).Should().OnlyHaveUniqueItems();
        foreach (var route in routes)
        {
            TelegramChannels.TryReadSeed(route.Channel.PipelineUrl, out string username).Should().BeTrue();
            OcharoSourceRegistry.TryReadHandle(SourceFamily.Telegram, route.Channel.PipelineUrl, out string handle).Should().BeTrue();
            handle.Should().Be("tg:" + username);
        }
    }

    [Fact]
    public void Wave_2_leaves_the_related_channels_and_the_ones_the_probes_could_not_read_as_they_were()
    {
        var channels = OcharoSourceRegistry.Current.Groups
            .SelectMany(group => group.Channels.Select(channel => (Key: $"{group.Id}/{channel.Id}", Channel: channel)))
            .ToDictionary(row => row.Key, row => row.Channel);
        // The preview of the first redirects to its channel page; the body of the other two timed out.
        string[] unreadable = ["hunarmand/telegram-announcements", "caex/venue-telegram", "human-house/gallery-telegram"];

        foreach (string key in Wave2Related.Concat(unreadable))
            (channels[key].Mode, channels[key].Method, channels[key].AdapterKey, channels[key].Setup, channels[key].Seed)
                .Should().Be(("manual", "manual-social", (string?)null, (string?)null, (string?)null), key);
        foreach (string key in Wave2Related)
            channels[key].Relationship.Should().Be("related", "only an owned channel harvests");
        Wave2.Should().HaveCount(10);
        (Wave2.Length + Wave2Related.Length).Should().Be(14, "the probe ledger routed 14 channels to telegram-preview");
    }

    [Fact]
    public void A_related_or_misspelled_telegram_channel_fails_the_registry_check()
    {
        var invalid = new Dictionary<string, Func<OcharoSourceRegistry>>
        {
            ["not owned"] = () => Registry(Channel("news", "telegram-preview", "https://t.me/s/demo_channel", relationship: "related")),
            ["not a preview page"] = () => Registry(Channel("news", "telegram-preview", "https://t.me/demo_channel")),
            ["a message page"] = () => Registry(Channel("news", "telegram-preview", "https://t.me/s/demo_channel/101")),
            ["a page cursor"] = () => Registry(Channel("news", "telegram-preview", "https://t.me/s/demo_channel?before=101")),
            ["another host"] = () => Registry(Channel("news", "telegram-preview", "https://telegram.example.test/s/demo_channel")),
            ["repeated seed"] = () => Registry(Channel("a", "telegram-preview", "https://t.me/s/demo_channel"),
                Channel("b", "telegram-preview", "https://t.me/s/demo_channel")),
        };

        Registry(Channel("news", "telegram-preview", "https://t.me/s/demo_channel")).Invoking(registry => registry.Validate()).Should().NotThrow();
        foreach (var (reason, build) in invalid)
            build().Invoking(registry => registry.Validate()).Should().Throw<InvalidOperationException>(reason);
    }

    [Fact]
    public async Task Default_setup_provisions_each_Wave_2_channel_as_its_own_telegram_source_on_the_shared_lane()
    {
        var registry = new FakeRegistrationBroker();
        var provisioner = new OcharoSourceProvisioner(registry, Delivery(registry));

        var setup = Value(await provisioner.EnsureAsync(enableSchedules: true));
        var repeat = Value(await provisioner.EnsureAsync(enableSchedules: true));

        repeat.Should().BeEquivalentTo(setup);
        var wave = setup.Sources.Where(route => route.SourceKey == TelegramSourceAdapter.AdapterKey).ToArray();
        wave.Select(route => $"{route.GroupId}/{route.ChannelId} {route.Seed}")
            .Should().BeEquivalentTo(Wave2.Select(row => $"{row.Channel} https://t.me/s/{row.Username}"));
        wave.Select(route => route.SourceId).Should().OnlyHaveUniqueItems();
        var sources = Value(await registry.ListSourcesAsync(setup.ContainerId)).Where(source => source.Family == SourceFamily.Telegram).ToArray();
        sources.Select(source => source.Handle).Should().BeEquivalentTo(Wave2.Select(row => "tg:" + row.Username));
        sources.Should().OnlyContain(source => source.ExternalRef == null && source.Posture == SourcePosture.OwnFetch
            && source.Completeness == SourceCompleteness.Window && source.Coverage == SourceCoverage.Filtered
            && source.RobotsPolicy == RobotsPolicy.Obey && !source.PoolShareable && source.Authority == 80 && source.CheckPolicy == "none");
        sources.Select(source => source.Name).Should().BeEquivalentTo(Wave2.Select(row => "@" + row.Username));
        var adapter = new TelegramSourceAdapter();
        sources.Should().OnlyContain(source => adapter.Serves(source));
        foreach (var route in wave)
        {
            var pipeline = Value(await registry.GetPipelineAsync(route.PipelineId));
            pipeline.Scope.Url.Should().Be(route.Seed);
            pipeline.Limits.MaxPages.Should().Be(3);
            pipeline.Name.Should().StartWith("Telegram · @").And.EndWith(" · Ocharo");
            var capture = pipeline.Schedules.Should().ContainSingle("a preview has no deletion check").Subject;
            (capture.Kind, capture.Mode, capture.Cron, capture.TimeZone, capture.Enabled)
                .Should().Be((WorkKind.Capture, ScheduleMode.Cron, "0 1,7,13,19 * * *", "Asia/Tashkent", true));
            adapter.ValidatePipeline(sources.Single(source => source.Id == route.SourceId), pipeline.Scope, pipeline.Limits)
                .IsSuccess.Should().BeTrue("the saved pipeline passes the reader's own check");
        }
        var status = Value(await provisioner.ReadAsync());
        status.Channels.Where(channel => wave.Any(route => route.ChannelId == channel.ChannelId && route.GroupId == channel.GroupId))
            .Should().HaveCount(10).And.OnlyContain(channel => channel.Pipeline != null);
    }

    private static OcharoSourceChannel Channel(string id, string adapterKey, string url, string relationship = "owned",
        string setup = "default") => new()
    {
        Id = id, Url = url, Role = "a synthetic channel", Relationship = relationship, Mode = "harvest", Method = "feed",
        Plan = "Read the synthetic channel.", AdapterKey = adapterKey, Setup = setup,
    };

    /// <summary>A registry of one synthetic publisher: its reference home page and the given harvest channels.</summary>
    private static OcharoSourceRegistry Registry(params OcharoSourceChannel[] channels) => new()
    {
        Format = "ocharo.event-sources.v1", ReviewedOn = new DateOnly(2026, 11, 21),
        Groups =
        [
            new()
            {
                Id = "synthetic", Name = "Synthetic publisher", Rank = 1, Tier = "P1", SourceType = "Media", Coverage = "Nowhere",
                Rationale = "A synthetic publisher.", EventFamilies = [], Aggregates = [],
                Channels =
                [
                    new()
                    {
                        Id = "home", Url = "https://home.example.test/", Role = "home page", Relationship = "owned", Primary = true,
                        Mode = "reference", Method = "reference", Plan = "None.",
                    },
                    .. channels,
                ],
            },
        ],
    };

    private static SourceModel Source(SourceFamily family, string handle) => new()
    {
        Id = Guid.NewGuid(), ContainerId = Guid.NewGuid(), Family = family, Handle = handle,
        Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window, Posture = SourcePosture.OwnFetch,
        Permission = SourcePermission.Open, PoolShareable = false, RobotsPolicy = RobotsPolicy.Obey,
        ConsentBasis = ConsentBasis.None, Authority = 60, RetentionClass = "standard", CapabilityPolicy = new(),
        State = SourceState.Active, StateSinceUtc = DateTimeOffset.UnixEpoch, CreatedAtUtc = DateTimeOffset.UnixEpoch,
    };

    private static FakeDeliveryBroker Delivery(FakeRegistrationBroker registry) =>
        new(new FakePostBroker(registry), registry, null, [new OcharoFashionFilter()]);

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success
        ? success.Value : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
}
