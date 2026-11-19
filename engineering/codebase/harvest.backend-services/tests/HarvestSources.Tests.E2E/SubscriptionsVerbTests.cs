using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Subscriptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;
using WoW.Two.Sdk.Beta.Be.Meta;

namespace HarvestSources.Tests.E2E;

/// <summary>The <c>subscriptions</c> host verb on the source host: Ocharo's cutover to a selector subscription — list, change,
/// preview, diff, explain and adopt — over posts seeded through <see cref="IPostBroker"/> with their Layer 1 tags in the envelope.
/// The diff compares against the host's own <c>ocharo-fashion-v1</c> filter.</summary>
public sealed class SubscriptionsVerbTests
{
    private const string FilterKey = OcharoFashionFilter.FilterKey;

    private static readonly string[] WeekTags = ["type:event", "domain:style-fashion/high-fashion", "domain:style-fashion", "topic:fashion-week"];
    private static readonly string[] BeautyTags = ["type:event", "domain:style-fashion", "domain:style-fashion/beauty", "topic:cosmetics", "topic:skin-care"];
    private static readonly string[] ShowTags = ["type:event", "domain:style-fashion", "domain:style-fashion/beauty", "topic:cosmetics", "topic:fashion-show"];
    private static readonly string[] TradeTags =
    [
        "type:event", "domain:business-and-finance/industries/apparel-industry", "domain:business-and-finance/industries", "domain:business-and-finance", "topic:textile",
    ];
    private static readonly string[] BuildTags = ["type:event", "domain:business-and-finance", "topic:building-materials"];
    private static readonly string[] SportsTags = ["type:event", "domain:sports", "topic:marathon"];

    [Fact]
    public async Task List_ShouldPrintTheTenantsSubscriptions()
    {
        await using var world = await World.StartAsync();
        var selector = await world.SelectorAsync();
        var channel = await world.ChannelAsync();
        var other = await world.OtherTenantSelectorAsync();

        var owned = await world.RunAsync("list", "--tenant", "ocharo");

        owned.Exit.Should().Be(0, owned.Error);
        owned.Error.Should().Contain($"{selector.Id:D} selector ocharo-events selector v1 active")
            .And.Contain($"{channel.Id:D} channel - {FilterKey} active")
            .And.NotContain(other.Id.ToString("D"));
        var rows = Last(owned.Output).GetProperty("subscriptions").EnumerateArray().ToArray();
        rows.Select(row => row.GetProperty("id").GetGuid()).Should().BeEquivalentTo([selector.Id, channel.Id]);
        var selectorRow = rows.Single(row => row.GetProperty("id").GetGuid() == selector.Id);
        selectorRow.GetProperty("tenant").GetString().Should().Be("ocharo");
        selectorRow.GetProperty("kind").GetString().Should().Be("selector");
        selectorRow.GetProperty("name").GetString().Should().Be("ocharo-events");
        selectorRow.GetProperty("selectorVersion").GetInt32().Should().Be(1);
        selectorRow.GetProperty("state").GetString().Should().Be("active");
        rows.Single(row => row.GetProperty("id").GetGuid() == channel.Id).GetProperty("filterKey").GetString().Should().Be(FilterKey);

        var everyone = await world.RunAsync("list");
        everyone.Exit.Should().Be(0, everyone.Error);
        Last(everyone.Output).GetProperty("subscriptions").GetArrayLength().Should().Be(3);
        (await world.RunAsync("list", "--tenant", "nobody")).Exit.Should().Be(1);
    }

    [Fact]
    public async Task Show_ShouldResolveAnIdOrAName_AndRefuseAnAmbiguousOne()
    {
        await using var world = await World.StartAsync();
        var selector = await world.SelectorAsync();
        var other = await world.OtherTenantSelectorAsync();

        var byName = await world.RunAsync("show", "--subscription", "ocharo-events", "--tenant", "ocharo");
        var byId = await world.RunAsync("show", "--subscription", other.Id.ToString("D"));
        var ambiguous = await world.RunAsync("show", "--subscription", "ocharo-events");

        byName.Exit.Should().Be(0, byName.Error);
        var shown = Last(byName.Output);
        shown.GetProperty("tenant").GetString().Should().Be("ocharo");
        shown.GetProperty("subscription").GetProperty("id").GetGuid().Should().Be(selector.Id);
        shown.GetProperty("subscription").GetProperty("selector").GetProperty("entityTypes").EnumerateArray().Select(item => item.GetString()).Should().Equal("event");
        byId.Exit.Should().Be(0, byId.Error);
        Last(byId.Output).GetProperty("tenant").GetString().Should().Be("other");
        ambiguous.Exit.Should().Be(1);
        ambiguous.Error.Should().Contain("names 2 subscriptions").And.Contain(selector.Id.ToString("D")).And.Contain(other.Id.ToString("D"));
        ambiguous.Output.Should().BeEmpty();
        (await world.RunAsync("show", "--subscription", "ocharo-events", "--tenant", "other")).Exit.Should().Be(0);
        (await world.RunAsync("show", "--subscription", selector.Id.ToString("D"), "--tenant", "other")).Exit.Should().Be(1, "the id belongs to another tenant");
        (await world.RunAsync("show", "--subscription", "no-such-name")).Exit.Should().Be(1);
    }

    [Fact]
    public async Task Selector_ShouldSetAVersion_AndPrintTheWindowCounts()
    {
        await using var world = await World.StartAsync();
        var subscription = await world.SelectorAsync();
        await world.SeedAsync();

        var stored = await world.RunAsync("selector", "--subscription", "ocharo-events", "--tenant", "ocharo");

        stored.Exit.Should().Be(0, stored.Error);
        var current = Last(stored.Output);
        current.GetProperty("selectorVersion").GetInt32().Should().Be(1);
        current.GetProperty("selector").GetProperty("topics").EnumerateArray().Select(item => item.GetString()).Should().Equal("fashion-show", "textile");

        var changed = world.OcharoLike() with { ExcludeDomains = ["style-fashion/high-fashion"] };
        var sinceDate = DateTime.UtcNow.AddDays(-3).Date;
        string since = sinceDate.ToString("yyyy-MM-dd");
        var set = await world.RunAsync("selector", "--subscription", "ocharo-events", "--tenant", "ocharo", "--set", Json(changed), "--since", since, "--by", "dev-check");

        set.Exit.Should().Be(0, set.Error);
        var counts = Last(set.Output);
        counts.GetProperty("subscriptionId").GetGuid().Should().Be(subscription.Id);
        counts.GetProperty("selectorVersion").GetInt32().Should().Be(2);
        counts.GetProperty("changed").GetBoolean().Should().BeTrue();
        counts.GetProperty("windowSinceUtc").GetDateTimeOffset().Date.Should().Be(sinceDate);
        (counts.GetProperty("considered").GetInt32(), counts.GetProperty("newMatches").GetInt32(), counts.GetProperty("unmatched").GetInt32(),
            counts.GetProperty("rematched").GetInt32(), counts.GetProperty("pending").GetInt32()).Should().Be((8, 0, 1, 0, 1),
            "the three held posts and every live post in the window are re-judged: the fashion week leaves, the untagged post waits");
        set.Error.Should().Contain("selector changed to v2").And.Contain("1 unmatched");
        (await world.CountAsync("SELECT count(*) FROM harvest_subscription_selectors WHERE subscription_id=@id", subscription.Id)).Should().Be(2);
        (await world.TextAsync("SELECT changed_by FROM harvest_subscription_selectors WHERE subscription_id=@id AND version=2", subscription.Id)).Should().Be("dev-check");
        (await world.CountAsync("SELECT count(*) FROM harvest_subscription_event WHERE subscription_id=@id AND type='post.unmatched' AND reason='resync'", subscription.Id))
            .Should().Be(1);

        var unknown = await world.RunAsync("selector", "--subscription", "ocharo-events", "--tenant", "ocharo", "--set",
            Json(changed with { Domains = [new SelectorDomain { Key = "no-such-node" }] }));
        unknown.Exit.Should().Be(1, "a taxonomy key the module does not know is refused, not a usage error");
        (await world.CountAsync("SELECT selector_version FROM harvest_subscriptions WHERE id=@id", subscription.Id)).Should().Be(2);
    }

    [Fact]
    public async Task Selector_ShouldKeepTheVersion_WhenUnchanged()
    {
        await using var world = await World.StartAsync();
        var subscription = await world.SelectorAsync();
        await world.SeedAsync();
        long events = await world.CountAsync("SELECT count(*) FROM harvest_subscription_event WHERE subscription_id=@id", subscription.Id);
        string stored = Last((await world.RunAsync("selector", "--subscription", "ocharo-events", "--tenant", "ocharo")).Output).GetProperty("selector").GetRawText();

        var same = await world.RunAsync("selector", "--subscription", "ocharo-events", "--tenant", "ocharo", "--set", stored);

        same.Exit.Should().Be(0, same.Error);
        var counts = Last(same.Output);
        counts.GetProperty("selectorVersion").GetInt32().Should().Be(1);
        counts.GetProperty("changed").GetBoolean().Should().BeFalse();
        counts.GetProperty("considered").GetInt32().Should().Be(0);
        same.Error.Should().Contain("selector unchanged at v1");
        (await world.CountAsync("SELECT count(*) FROM harvest_subscription_selectors WHERE subscription_id=@id", subscription.Id)).Should().Be(1);
        (await world.CountAsync("SELECT count(*) FROM harvest_subscription_event WHERE subscription_id=@id", subscription.Id)).Should().Be(events);
    }

    [Fact]
    public async Task State_ShouldArchive_AndRefuseReactivation()
    {
        await using var world = await World.StartAsync();
        var subscription = await world.SelectorAsync();
        var channel = await world.ChannelAsync();

        var suspended = await world.RunAsync("state", "--subscription", "ocharo-events", "--tenant", "ocharo", "--state", "suspended");
        var resumed = await world.RunAsync("state", "--subscription", "ocharo-events", "--tenant", "ocharo", "--state", "ACTIVE");
        var archived = await world.RunAsync("state", "--subscription", "ocharo-events", "--tenant", "ocharo", "--state", "archived");
        var again = await world.RunAsync("state", "--subscription", "ocharo-events", "--tenant", "ocharo", "--state", "archived");
        var reactivated = await world.RunAsync("state", "--subscription", "ocharo-events", "--tenant", "ocharo", "--state", "active");

        Last(suspended.Output).GetProperty("state").GetString().Should().Be("suspended");
        suspended.Error.Should().Contain("active -> suspended");
        Last(resumed.Output).GetProperty("state").GetString().Should().Be("active");
        archived.Exit.Should().Be(0, archived.Error);
        Last(archived.Output).GetProperty("state").GetString().Should().Be("archived");
        again.Exit.Should().Be(0, "the same state answers unchanged");
        again.Error.Should().Contain("already archived");
        reactivated.Exit.Should().Be(1);
        reactivated.Error.Should().Contain("stays archived");
        reactivated.Output.Should().BeEmpty();
        (await world.TextAsync("SELECT state FROM harvest_subscriptions WHERE id=@id", subscription.Id)).Should().Be("archived");
        (await world.RunAsync("selector", "--subscription", "ocharo-events", "--tenant", "ocharo", "--set", Json(world.OcharoLike()))).Exit
            .Should().Be(1, "an archived subscription keeps its selector");

        var retired = await world.RunAsync("state", "--subscription", channel.Id.ToString("D"), "--state", "archived");
        retired.Exit.Should().Be(0, retired.Error);
        Last(retired.Output).GetProperty("kind").GetString().Should().Be("channel");
        (await world.RunAsync("list", "--tenant", "ocharo")).Error.Should().Contain($"{channel.Id:D} channel - {FilterKey} archived");
    }

    [Fact]
    public async Task Diff_ShouldCountAndListDisagreements()
    {
        await using var world = await World.StartAsync();
        await world.SelectorAsync();
        var posts = await world.SeedAsync();

        var diff = await world.RunAsync("diff", "--subscription", "ocharo-events", "--tenant", "ocharo", "--against", FilterKey, "--pending");

        diff.Exit.Should().Be(0, diff.Error);
        var result = Last(diff.Output);
        result.GetProperty("subscription").GetString().Should().Be("ocharo-events");
        result.GetProperty("selectorVersion").GetInt32().Should().Be(1);
        result.GetProperty("filterKey").GetString().Should().Be(FilterKey);
        (result.GetProperty("posts").GetInt32(), result.GetProperty("outOfScope").GetInt32(), result.GetProperty("both").GetInt32(),
            result.GetProperty("filterOnly").GetInt32(), result.GetProperty("selectorOnly").GetInt32(), result.GetProperty("neither").GetInt32(),
            result.GetProperty("pending").GetInt32(), result.GetProperty("filterUndecided").GetInt32())
            .Should().Be((8, 1, 2, 2, 1, 1, 1, 0));
        var items = result.GetProperty("items").EnumerateArray().ToArray();
        items.Select(item => item.GetProperty("postId").GetGuid()).Should().Equal([.. InPostOrder(posts.Build, posts.Beauty), posts.Show, posts.Waiting],
            "the filter-only posts come first, then the selector-only one, then the pending one");

        var beauty = items.Single(item => item.GetProperty("postId").GetGuid() == posts.Beauty);
        (beauty.GetProperty("filter").GetString(), beauty.GetProperty("selector").GetString()).Should().Be(("yes", "noMatch"));
        Texts(beauty.GetProperty("clauses")).Should().Equal("except:style-fashion/beauty");
        Texts(beauty.GetProperty("topics")).Should().Equal("cosmetics", "skin-care");
        var show = items.Single(item => item.GetProperty("postId").GetGuid() == posts.Show);
        (show.GetProperty("filter").GetString(), show.GetProperty("selector").GetString()).Should().Be(("no", "match"));
        Texts(show.GetProperty("clauses")).Should().Equal("type:event", "topic:fashion-show");
        show.GetProperty("title").GetString().Should().Be("Beauty expo");
        show.GetProperty("source").GetString().Should().Be("site/afisha.example");
        var build = items.Single(item => item.GetProperty("postId").GetGuid() == posts.Build);
        (build.GetProperty("filter").GetString(), build.GetProperty("selector").GetString()).Should().Be(("yes", "noMatch"));
        Texts(build.GetProperty("clauses")).Should().Equal("no-subject");
        var waiting = items[^1];
        waiting.GetProperty("selector").GetString().Should().Be("pending");
        Texts(waiting.GetProperty("clauses")).Should().Equal("untagged");
        diff.Error.Should().Contain("8 posts, 1 out of scope, 2 both, 2 filter only, 1 selector only, 1 neither, 1 pending")
            .And.Contain($"{posts.Beauty:D} filter yes selector noMatch [except:style-fashion/beauty]");

        var listed = await world.RunAsync("diff", "--subscription", "ocharo-events", "--tenant", "ocharo", "--against", FilterKey);
        Last(listed.Output).GetProperty("items").GetArrayLength().Should().Be(3, "pending posts are listed only on ask");
        var capped = await world.RunAsync("diff", "--subscription", "ocharo-events", "--tenant", "ocharo", "--against", FilterKey, "--limit", "2");
        Last(capped.Output).GetProperty("items").GetArrayLength().Should().Be(2);
        Last(capped.Output).GetProperty("filterOnly").GetInt32().Should().Be(2);
        Last(capped.Output).GetProperty("selectorOnly").GetInt32().Should().Be(1, "the limit caps the list, never the counts");
        var counts = await world.RunAsync("diff", "--subscription", "ocharo-events", "--tenant", "ocharo", "--against", FilterKey, "--counts-only");
        Last(counts.Output).GetProperty("items").GetArrayLength().Should().Be(0);
        Last(counts.Output).GetProperty("both").GetInt32().Should().Be(2);
        (await world.RunAsync("diff", "--subscription", "ocharo-events", "--tenant", "ocharo", "--against", "no-such-filter-v1")).Exit.Should().Be(1);
        (await world.RunAsync("diff", "--subscription", "ocharo-events", "--tenant", "ocharo", "--against", FilterKey, "--limit", "1001")).Exit.Should().Be(1);
        (await world.RunAsync("diff", "--subscription", "ocharo-events", "--tenant", "ocharo", "--against", FilterKey, "--limit", "0")).Exit.Should().Be(2);
    }

    [Fact]
    public async Task Explain_ShouldPrintTheVerdict()
    {
        await using var world = await World.StartAsync();
        await world.SelectorAsync();
        var posts = await world.SeedAsync();

        var held = await world.RunAsync("explain", "--subscription", "ocharo-events", "--tenant", "ocharo", "--post", posts.Week.ToString("D"));

        held.Exit.Should().Be(0, held.Error);
        var week = Last(held.Output);
        week.GetProperty("verdict").GetString().Should().Be("match");
        week.GetProperty("held").GetString().Should().Be("held");
        week.GetProperty("revision").GetInt32().Should().Be(1);
        Texts(week.GetProperty("clauses")).Should().Equal("type:event", "domain:style-fashion");
        week.GetProperty("context").EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.GetString())
            .Should().Equal(new Dictionary<string, string?> { ["catalog"] = "ocharo-fashion", ["defaultKind"] = "exhibition" });
        week.GetProperty("entityType").GetString().Should().Be("event");
        Texts(week.GetProperty("domains")).Should().Equal("style-fashion/high-fashion");
        Texts(week.GetProperty("topics")).Should().Equal("fashion-week");
        held.Error.Should().Contain("is match [type:event, domain:style-fashion], log holds held");

        var beauty = Last((await world.RunAsync("explain", "--subscription", "ocharo-events", "--tenant", "ocharo", "--post", posts.Beauty.ToString("D"))).Output);
        (beauty.GetProperty("verdict").GetString(), beauty.GetProperty("held").GetString()).Should().Be(("noMatch", "none"));
        Texts(beauty.GetProperty("clauses")).Should().Equal("except:style-fashion/beauty");
        var show = Last((await world.RunAsync("explain", "--subscription", "ocharo-events", "--tenant", "ocharo", "--post", posts.Show.ToString("D"))).Output);
        (show.GetProperty("verdict").GetString(), show.GetProperty("held").GetString()).Should().Be(("match", "held"));
        Texts(show.GetProperty("clauses")).Should().Equal("type:event", "topic:fashion-show");
        var waiting = Last((await world.RunAsync("explain", "--subscription", "ocharo-events", "--tenant", "ocharo", "--post", posts.Waiting.ToString("D"))).Output);
        (waiting.GetProperty("verdict").GetString(), waiting.GetProperty("held").GetString()).Should().Be(("pending", "none"));
        Texts(waiting.GetProperty("clauses")).Should().Equal("untagged");

        var dropped = world.OcharoLike() with { ExcludeDomains = ["style-fashion/high-fashion"] };
        (await world.RunAsync("selector", "--subscription", "ocharo-events", "--tenant", "ocharo", "--set", Json(dropped))).Exit.Should().Be(0);
        var unmatched = Last((await world.RunAsync("explain", "--subscription", "ocharo-events", "--tenant", "ocharo", "--post", posts.Week.ToString("D"))).Output);
        (unmatched.GetProperty("verdict").GetString(), unmatched.GetProperty("held").GetString(), unmatched.GetProperty("selectorVersion").GetInt32())
            .Should().Be(("noMatch", "unmatched", 2));
        Texts(unmatched.GetProperty("clauses")).Should().Equal("exclude-domain:style-fashion/high-fashion");

        var missing = await world.RunAsync("explain", "--subscription", "ocharo-events", "--tenant", "ocharo", "--post", Guid.NewGuid().ToString("D"));
        missing.Exit.Should().Be(1);
        missing.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task Adopt_ShouldUnmatchOnce()
    {
        await using var world = await World.StartAsync();
        var channel = await world.ChannelAsync();
        var posts = await world.SeedAsync();
        var selector = await world.SelectorAsync(BackfillMode.None);
        string[] adopt = ["adopt", "--into", "ocharo-events", "--from", channel.Id.ToString("D"), "--tenant", "ocharo"];

        var dry = await world.RunAsync([.. adopt, "--dry-run"]);

        dry.Exit.Should().Be(0, dry.Error);
        AdoptCounts(dry.Output).Should().Be((5, 0, 2, 2, 1, true));
        dry.Error.Should().Contain("dry run, nothing written");
        (await world.EventCountAsync(selector.Id)).Should().Be(0, "a dry run rolls back");

        var real = await world.RunAsync(adopt);

        real.Exit.Should().Be(0, real.Error);
        AdoptCounts(real.Output).Should().Be((5, 0, 2, 2, 1, false));
        Last(real.Output).GetProperty("into").GetString().Should().Be("ocharo-events");
        (await world.EventCountAsync(selector.Id)).Should().Be(4);
        (await world.CountAsync("SELECT count(*) FROM harvest_subscription_event WHERE subscription_id=@id AND type='post.unmatched' AND reason='resync'", selector.Id)).Should().Be(2);
        (await world.CountAsync("SELECT count(*) FROM harvest_subscription_event WHERE subscription_id=@id AND type='post.new' AND reason='backfill'", selector.Id)).Should().Be(2);
        (await world.CountAsync($"SELECT count(*) FROM harvest_subscription_event WHERE subscription_id=@id AND type='post.unmatched' AND post_id IN ('{posts.Build}','{posts.Beauty}')",
            selector.Id)).Should().Be(2, "CAEx Build & Co and the beauty expo leave Ocharo's list");

        var second = await world.RunAsync(adopt);

        second.Exit.Should().Be(0, second.Error);
        AdoptCounts(second.Output).Should().Be((5, 4, 0, 0, 1, false), "a second run writes nothing");
        (await world.EventCountAsync(selector.Id)).Should().Be(4);

        (await world.RunAsync("adopt", "--into", "ocharo-events", "--from", Guid.NewGuid().ToString("D"), "--tenant", "ocharo")).Exit.Should().Be(1);
        (await world.RunAsync("adopt", "--into", channel.Id.ToString("D"), "--from", selector.Id.ToString("D"))).Exit.Should().Be(1, "only a selector subscription adopts");
    }

    [Fact]
    public async Task Preview_ShouldPrintCountsPerClauseAndSource_WithScrubbedTitles()
    {
        await using var world = await World.StartAsync();
        await world.SelectorAsync();
        await world.ChannelAsync();
        var posts = await world.SeedAsync();
        await world.SaveAsync("contact", "Fashion fair, write info@example.com or call +998 90 123 45 67 today", "A fair.", WeekTags);
        string since = DateTime.UtcNow.AddDays(-3).ToString("yyyy-MM-dd");

        var preview = await world.RunAsync("preview", "--subscription", "ocharo-events", "--tenant", "ocharo", "--since", since, "--samples", "5");

        preview.Exit.Should().Be(0, preview.Error);
        var result = Last(preview.Output);
        result.GetProperty("subscription").GetString().Should().Be("ocharo-events");
        (result.GetProperty("considered").GetInt32(), result.GetProperty("tagged").GetInt32(), result.GetProperty("matched").GetInt32(),
            result.GetProperty("noMatch").GetInt32(), result.GetProperty("pending").GetInt32()).Should().Be((9, 8, 4, 4, 1));
        var clauses = result.GetProperty("clauses").EnumerateArray().ToDictionary(clause => clause.GetProperty("clause").GetString()!);
        clauses["except:style-fashion/beauty"].GetProperty("removes").GetInt32().Should().Be(1, "dropping the beauty exception admits the beauty expo");
        clauses["topic:fashion-show"].GetProperty("adds").GetInt32().Should().Be(1, "the beauty expo with a show passes only on its topic");
        var sources = result.GetProperty("sources").EnumerateArray().ToArray();
        var afisha = sources.Should().ContainSingle("a post no source clause admits has no context link, so no source row counts it").Which;
        (afisha.GetProperty("considered").GetInt32(), afisha.GetProperty("matched").GetInt32(), afisha.GetProperty("pending").GetInt32()).Should().Be((8, 4, 1));
        var samples = result.GetProperty("samples").EnumerateArray().ToArray();
        samples.Should().HaveCount(4);
        samples.Select(sample => sample.GetProperty("title").GetString()).Should().Contain("Fashion fair, write … or call … today");
        samples.Select(sample => sample.GetProperty("postId").GetGuid()).Should().Contain([posts.Week, posts.Show, posts.Texworld]);
        result.GetProperty("nearMisses").EnumerateArray().Should().NotBeEmpty();
        preview.Error.Should().Contain("9 considered, 8 tagged, 4 matched, 4 no match, 1 pending").And.Contain("except:style-fashion/beauty adds 0 removes 1");
        preview.Output.Should().NotContain("info@example.com").And.NotContain("123 45 67");

        (await world.RunAsync("preview", "--subscription", "ocharo-events", "--tenant", "ocharo", "--samples", "0")).Exit.Should().Be(0);
        (await world.RunAsync("preview", "--subscription", "ocharo-events", "--tenant", "ocharo", "--samples", "51")).Exit.Should().Be(1);
        (await world.RunAsync("preview", "--subscription", world.ChannelId.ToString("D"))).Exit.Should().Be(1, "a channel subscription has no selector");
    }

    [Theory]
    [InlineData("subscriptions")]
    [InlineData("subscriptions unknown")]
    [InlineData("subscriptions list --force")]
    [InlineData("subscriptions list --tenant")]
    [InlineData("subscriptions show")]
    [InlineData("subscriptions show --subscription")]
    [InlineData("subscriptions show --subscription a --subscription b")]
    [InlineData("subscriptions selector --subscription a --by someone")]
    [InlineData("subscriptions selector --subscription a --set {bad")]
    [InlineData("subscriptions selector --subscription a --set {\"excludeDomain\":[]}")]
    [InlineData("subscriptions selector --subscription a --set {} --since yesterday")]
    [InlineData("subscriptions state --subscription a")]
    [InlineData("subscriptions state --subscription a --state paused")]
    [InlineData("subscriptions preview --subscription a --samples many")]
    [InlineData("subscriptions diff --subscription a")]
    [InlineData("subscriptions diff --subscription a --against f --limit 0")]
    [InlineData("subscriptions diff --subscription a --against f --counts-only --pending")]
    [InlineData("subscriptions diff --subscription a --against f --counts-only --limit 5")]
    [InlineData("subscriptions diff --subscription a --against f --pending --pending")]
    [InlineData("subscriptions explain --subscription a")]
    [InlineData("subscriptions explain --subscription a --post nope")]
    [InlineData("subscriptions adopt --into a")]
    [InlineData("subscriptions adopt --from 3f2c8a0e-6d1b-4b8e-9d57-0a1b2c3d4e5f")]
    [InlineData("subscriptions adopt --into a --from nope")]
    public async Task Usage_ShouldExitTwo(string commandLine)
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
            verbs => verbs.AddHostVerb(SubscriptionsVerb.Name, (services, verb, ct) =>
                SubscriptionsVerb.RunAsync(services, verb.Arguments, verb.Output, verb.Error, ct)),
            output, error, CancellationToken.None);

        exit.Should().Be(2, error.ToString());
        error.ToString().Should().Contain("Usage: subscriptions list");
        output.ToString().Should().BeEmpty();
    }

    private static JsonElement Last(string output) => JsonDocument.Parse(output.TrimEnd().Split('\n')[^1]).RootElement;

    private static string[] Texts(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];

    private static string Json(SubscriptionSelector selector) => JsonSerializer.Serialize(selector, JsonOptionsConstants.Default);

    private static Guid[] InPostOrder(params Guid[] ids) => [.. ids.OrderBy(id => id.ToString("N"), StringComparer.Ordinal)];

    private static (int Considered, int AlreadyHeld, int Unmatched, int New, int Pending, bool DryRun) AdoptCounts(string output)
    {
        var result = Last(output);
        return (result.GetProperty("considered").GetInt32(), result.GetProperty("alreadyHeld").GetInt32(), result.GetProperty("unmatched").GetInt32(),
            result.GetProperty("new").GetInt32(), result.GetProperty("pending").GetInt32(), result.GetProperty("dryRun").GetBoolean());
    }

    /// <summary>The posts every case shares: Ocharo's selector in miniature (style-fashion without beauty, the apparel industry, the
    /// topics <c>fashion-show</c> and <c>textile</c>) over one source group, against the rules filter.</summary>
    private sealed record Seeded(Guid Week, Guid Build, Guid Beauty, Guid Show, Guid Texworld, Guid Marathon, Guid Waiting, Guid Outside);

    private sealed class World : IAsyncDisposable
    {
        private readonly Guid sourceId;
        private readonly Guid secondSourceId;
        private readonly Guid pipelineId;
        private Guid? channelId;

        private World(WebApplication host, string connectionString, TenantModel tenant, ContainerModel container, Guid groupId, Guid sourceId, Guid secondSourceId, Guid pipelineId)
        {
            Host = host;
            ConnectionString = connectionString;
            Tenant = tenant;
            Container = container;
            GroupId = groupId;
            this.sourceId = sourceId;
            this.secondSourceId = secondSourceId;
            this.pipelineId = pipelineId;
        }

        internal WebApplication Host { get; }
        internal string ConnectionString { get; }
        internal TenantModel Tenant { get; }
        internal ContainerModel Container { get; }
        internal Guid GroupId { get; }
        internal Guid ChannelId => channelId ?? throw new InvalidOperationException("No channel subscription was made.");

        internal static async Task<World> StartAsync()
        {
            string database = await SourcePostgres.NewDatabaseAsync();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(Program).Assembly.GetName().Name,
                EnvironmentName = "Development",
            });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseSettings:ConnectionString"] = database,
                ["Harvest:Operator:ApiKey"] = "hv_" + new string('s', 32),
                ["Harvest:Worker:Enabled"] = "false",
                ["AllowedHosts"] = "localhost",
                ["Logging:LogLevel:Default"] = "Warning",
            });
            builder.WebHost.UseTestServer();
            builder.Configure();
            var host = builder.Build();
            await host.ConfigureAsync();

            await using var scope = host.Services.CreateAsyncScope();
            var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
            var tenant = (await registry.RegisterTenantAsync(new TenantRegistration { Slug = "ocharo", Name = "Ocharo" })).ValueOrThrow();
            var container = (await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id })).ValueOrThrow();
            var group = (await registry.DeclareSourceGroupAsync(new SourceGroupDeclaration { ContainerId = container.Id, Name = "Afisha" })).ValueOrThrow();
            async Task<SourceModel> SourceAsync(string handle) => (await registry.RegisterSourceAsync(new SourceRegistration
            {
                ContainerId = container.Id, Family = SourceFamily.Site, Handle = handle, Coverage = SourceCoverage.Whole,
                Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
            })).ValueOrThrow();
            var source = await SourceAsync("afisha.example");
            (await registry.UpdateSourceAsync(source.Id, new SourceUpdate { GroupId = group.Id })).ValueOrThrow();
            var second = await SourceAsync("second.example");
            var pipeline = (await registry.DeclarePipelineAsync(new PipelineDeclaration { ContainerId = container.Id, SourceId = source.Id })).ValueOrThrow();
            return new World(host, database, tenant, container, group.Id, source.Id, second.Id, pipeline.Id);
        }

        /// <summary>Ocharo's selector in miniature, over the Afisha group with the context its events carry.</summary>
        internal SubscriptionSelector OcharoLike() => new()
        {
            EntityTypes = ["event"],
            Domains =
            [
                new SelectorDomain { Key = "style-fashion", Except = ["style-fashion/beauty"] },
                new SelectorDomain { Key = "business-and-finance/industries/apparel-industry" },
            ],
            Topics = ["fashion-show", "textile"],
            Sources = new SelectorSources
            {
                Include = [new SelectorSourceRef { GroupId = GroupId, Context = new Dictionary<string, string> { ["defaultKind"] = "exhibition" } }],
            },
        };

        internal async Task<SubscriptionModel> SelectorAsync(BackfillMode backfill = BackfillMode.Live)
        {
            await using var scope = Host.Services.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<IDeliveryBroker>().SubscribeAsync(new SubscriptionDeclaration
            {
                TenantId = Tenant.Id, ContainerId = Container.Id, Name = "ocharo-events", Selector = OcharoLike(), Schema = PostEnvelopeConstants.EventSchema,
                Context = new Dictionary<string, string> { ["catalog"] = "ocharo-fashion" }, Backfill = backfill,
            })).ValueOrThrow();
        }

        /// <summary>Subscribes the per-pipeline channel Ocharo has today, behind the rules filter.</summary>
        internal async Task<SubscriptionModel> ChannelAsync()
        {
            await using var scope = Host.Services.CreateAsyncScope();
            var channel = (await scope.ServiceProvider.GetRequiredService<IDeliveryBroker>().SubscribeAsync(new SubscriptionDeclaration
            {
                TenantId = Tenant.Id, ContainerId = Container.Id, PipelineId = pipelineId, FilterKey = FilterKey, Schema = PostEnvelopeConstants.EventSchema,
            })).ValueOrThrow();
            channelId = channel.Id;
            return channel;
        }

        /// <summary>Subscribes a second tenant's selector under the same name.</summary>
        internal async Task<SubscriptionModel> OtherTenantSelectorAsync()
        {
            await using var scope = Host.Services.CreateAsyncScope();
            var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
            var other = (await registry.RegisterTenantAsync(new TenantRegistration { Slug = "other", Name = "Other" })).ValueOrThrow();
            var container = (await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = other.Id })).ValueOrThrow();
            return (await scope.ServiceProvider.GetRequiredService<IDeliveryBroker>().SubscribeAsync(new SubscriptionDeclaration
            {
                TenantId = other.Id, ContainerId = container.Id, Name = "ocharo-events",
                Selector = new SubscriptionSelector { EntityTypes = ["event"], Domains = [new SelectorDomain { Key = "style-fashion" }] },
            })).ValueOrThrow();
        }

        /// <summary>Saves eight posts: a fashion week and a textile fair the selector passes, CAEx Build and Co and a beauty expo the
        /// rules pass and it holds back, a beauty expo with a fashion-show topic it passes and the rules miss, a marathon both refuse,
        /// an untagged post it cannot judge yet, and a fashion week of a source outside its group.</summary>
        internal async Task<Seeded> SeedAsync() => new(
            Week: await SaveAsync("week", "Tashkent Fashion Week", "A fashion week in the capital.", WeekTags),
            Build: await SaveAsync("build", "Build & Co", "Выставка строительных материалов: аксессуары для дома.", BuildTags),
            Beauty: await SaveAsync("beauty", "Beauty and fashion expo", "Cosmetics and skin care at the expo.", BeautyTags),
            Show: await SaveAsync("show", "Beauty expo", "Cosmetics, skin care and a catwalk show.", ShowTags),
            Texworld: await SaveAsync("texworld", "Texworld", "A textile trade fair.", TradeTags),
            Marathon: await SaveAsync("marathon", "City marathon", "A marathon through the old town.", SportsTags),
            Waiting: await SaveAsync("waiting", "Fashion news", "A fashion show next week.", null),
            Outside: await SaveAsync("outside", "Fashion fair elsewhere", "A fashion fair.", WeekTags, secondSourceId));

        internal async Task<Guid> SaveAsync(string key, string title, string text, string[]? tags, Guid? from = null)
        {
            await using var scope = Host.Services.CreateAsyncScope();
            var capture = new Capture
            {
                ExternalKey = key, Url = $"https://afisha.example/{key}", RequestUrl = $"https://afisha.example/{key}", FinalUrl = $"https://afisha.example/{key}",
                CapturedAtUtc = DateTimeOffset.UtcNow, Body = Encoding.UTF8.GetBytes(text), ContentProjection = $"{title}\n{text}", HttpStatus = 200, ContentType = "text/html",
            };
            var envelope = new PostEnvelope
            {
                Kind = PostKind.Event, Title = title, Text = text, Language = new PostLanguage { Code = "en" },
                Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
                Tags = [.. (tags ?? []).Select(tag => new PostTag { Tag = tag, Tier = ExtractionTier.SmallModel })],
            };
            return (await scope.ServiceProvider.GetRequiredService<IPostBroker>().SaveCaptureAsync(Container.Id, from ?? sourceId, null, capture, envelope)).ValueOrThrow().Post.Id;
        }

        internal Task<long> EventCountAsync(Guid subscriptionId) =>
            CountAsync("SELECT count(*) FROM harvest_subscription_event WHERE subscription_id=@id", subscriptionId);

        internal async Task<long> CountAsync(string sql, Guid id) => Convert.ToInt64(await ScalarAsync(sql, id));

        internal async Task<string> TextAsync(string sql, Guid id) => (string)(await ScalarAsync(sql, id))!;

        internal async Task<(int Exit, string Output, string Error)> RunAsync(params string[] arguments)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exit = await SubscriptionsVerb.RunAsync(Host.Services, arguments, output, error, CancellationToken.None);
            return (exit, output.ToString(), error.ToString());
        }

        private async Task<object?> ScalarAsync(string sql, Guid id)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", id);
            return await command.ExecuteScalarAsync();
        }

        public async ValueTask DisposeAsync() => await Host.DisposeAsync();
    }
}
