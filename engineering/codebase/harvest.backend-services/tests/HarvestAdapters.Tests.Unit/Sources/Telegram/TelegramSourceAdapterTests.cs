using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Telegram;

namespace HarvestAdapters.Tests.Unit.Sources.Telegram;

public sealed class TelegramSourceAdapterTests
{
    private static readonly TelegramSourceAdapter Adapter = new();

    [Theory]
    [InlineData(SourceFamily.Telegram, "tg:demo_channel", true)]
    [InlineData(SourceFamily.Telegram, "tg:abc", false)]
    [InlineData(SourceFamily.Telegram, "demo_channel", false)]
    [InlineData(SourceFamily.Telegram, "tg:Demo_Channel", false)]
    [InlineData(SourceFamily.Telegram, "t.me", false)]
    [InlineData(SourceFamily.Site, "tg:demo_channel", false)]
    [InlineData(SourceFamily.Instagram, "demo_channel", false)]
    public void Serves_only_a_telegram_source_with_the_registry_handle_of_a_channel(SourceFamily family, string handle, bool serves) =>
        Adapter.Serves(Source(family, handle)).Should().Be(serves);

    [Fact]
    public void Every_channel_shares_the_t_me_lane_and_waits_ten_seconds_between_requests()
    {
        var first = Adapter.Describe(Source(SourceFamily.Telegram, "tg:demo_channel"));
        var second = Adapter.Describe(Source(SourceFamily.Telegram, "tg:other_channel"));

        first.SourceUrl.Should().Be("https://t.me/s/demo_channel");
        second.SourceUrl.Should().Be("https://t.me/s/other_channel");
        first.HostKey.Should().Be("t.me").And.Be(second.HostKey);
        var step = first.Steps.Should().ContainSingle().Subject;
        (step.Key, step.Role, step.Requires.MinDelay).Should().Be(("enumerate", StepRole.Enumerate, (TimeSpan?)TimeSpan.FromSeconds(10)));
        first.Plans[WorkKind.Capture].Should().Equal("enumerate");
        first.Plans[WorkKind.Sweep].Should().Equal("enumerate");
        first.Plans.Should().NotContainKey(WorkKind.Check, "a preview offers no deletion check");
        Adapter.Key.Should().Be("telegram-preview");
        Adapter.Family.Should().Be(SourceFamily.Telegram);
    }

    [Theory]
    [InlineData(null, 3)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(25, 3)]
    public void A_capture_run_never_reads_more_than_three_pages(int? saved, int resolved)
    {
        var source = Source(SourceFamily.Telegram, "tg:demo_channel");
        var limits = new PipelineLimits { MaxPages = saved, MaxItems = 150 };

        Adapter.ResolveRunLimits(source, limits, WorkKind.Capture).Should().Be(limits with { MaxPages = resolved });
        Adapter.ResolveRunLimits(source, limits, WorkKind.Sweep).MaxPages.Should().Be(resolved);
        Adapter.ResolveRunLimits(source, limits, WorkKind.Check).Should().Be(limits);
    }

    [Theory]
    [InlineData("https://t.me/s/demo_channel", null, false, 3, true)]
    [InlineData(null, null, false, 3, true)]
    [InlineData("https://t.me/s/Demo_Channel/", null, false, null, true)]
    [InlineData("https://t.me/s/other_channel", null, false, 3, false)]
    [InlineData("https://t.me/demo_channel", null, false, 3, false)]
    [InlineData("https://t.me/s/demo_channel?before=5", null, false, 3, false)]
    [InlineData("https://t.me/s/demo_channel", "before", false, 3, false)]
    [InlineData("https://t.me/s/demo_channel", null, true, 3, false)]
    [InlineData("https://t.me/s/demo_channel", null, false, 4, false)]
    [InlineData("https://t.me/s/demo_channel", null, false, 0, false)]
    public void A_pipeline_may_only_read_its_own_channels_preview_within_three_pages(string? url, string? parameter, bool restrict,
        int? maxPages, bool valid)
    {
        var scope = new PipelineScope
        {
            Url = url, RestrictToPath = restrict,
            Parameters = parameter is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [parameter] = "1" },
        };

        var result = Adapter.ValidatePipeline(Source(SourceFamily.Telegram, "tg:demo_channel"), scope, new() { MaxPages = maxPages, MaxItems = 150 });

        result.IsSuccess.Should().Be(valid);
    }

    [Fact]
    public void The_console_reads_the_channels_own_preview_as_the_saved_scope()
    {
        var model = Adapter.DescribeConfiguration(Source(SourceFamily.Telegram, "tg:demo_channel"));

        model.Readiness.Should().Be(SourceReadiness.Ready);
        model.ScopeSchema.GetProperty("properties").GetProperty("url").GetProperty("const").GetString().Should().Be("https://t.me/s/demo_channel");
        model.ScopeSchema.GetProperty("limits").GetProperty("maxPages").GetProperty("maximum").GetInt32().Should().Be(3);
        model.PostView.ShowSourceText.Should().BeTrue();
    }

    [Fact]
    public void The_reader_takes_precedence_for_a_telegram_source_and_serves_no_other_family()
    {
        using var provider = new ServiceCollection().AddHarvestReaders().AddOcharoEventSources().BuildServiceProvider();
        var adapters = provider.GetServices<ISourceAdapter>().ToList();

        adapters.Last(adapter => adapter.Serves(Source(SourceFamily.Telegram, "tg:demo_channel"))).Key.Should().Be("telegram-preview");
        adapters.Where(adapter => adapter.Serves(Source(SourceFamily.Telegram, "tg:demo_channel"))).Should().ContainSingle();
        adapters.Where(adapter => adapter.Key == "telegram-preview" && adapter.Serves(Source(SourceFamily.Site, "t.me"))).Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://t.me/s/demo_channel", "demo_channel")]
    [InlineData("https://t.me/s/Demo_Channel/", "demo_channel")]
    public void A_registry_seed_names_its_channel_and_its_handle(string seed, string username)
    {
        TelegramChannels.TryReadSeed(seed, out string read).Should().BeTrue();
        read.Should().Be(username);
        OcharoSourceRegistry.TryReadHandle(SourceFamily.Telegram, seed, out string handle).Should().BeTrue();
        handle.Should().Be("tg:" + username);
        TelegramChannels.TryReadHandle(handle, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://t.me/demo_channel")]
    [InlineData("https://t.me/s/demo_channel?before=5")]
    [InlineData("https://example.test/s/demo_channel")]
    [InlineData("not a url")]
    public void A_seed_that_is_not_a_channels_newest_preview_names_nothing(string seed)
    {
        TelegramChannels.TryReadSeed(seed, out _).Should().BeFalse();
        OcharoSourceRegistry.TryReadHandle(SourceFamily.Telegram, seed, out _).Should().BeFalse();
    }

    private static SourceModel Source(SourceFamily family, string handle) => new()
    {
        Id = Guid.NewGuid(), ContainerId = Guid.NewGuid(), Family = family, Handle = handle,
        Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window, Posture = SourcePosture.OwnFetch,
        Permission = SourcePermission.Open, PoolShareable = false, RobotsPolicy = RobotsPolicy.Obey,
        ConsentBasis = ConsentBasis.None, Authority = 80, RetentionClass = "standard", CapabilityPolicy = new(),
        State = SourceState.Active, StateSinceUtc = DateTimeOffset.UnixEpoch, CreatedAtUtc = DateTimeOffset.UnixEpoch,
    };
}
