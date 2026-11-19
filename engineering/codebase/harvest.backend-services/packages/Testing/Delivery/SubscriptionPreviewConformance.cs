using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;

/// <summary>Specifies the selector preview shared by the in-process module, HTTP form and consumer fakes: what a selector
/// would match over a window of a container's posts, counted per verdict, source and clause, before a subscription holds it.</summary>
public abstract class SubscriptionPreviewConformance
{
    private const string Beauty = "style-fashion/beauty";

    private static readonly SubscriptionSelector Fashion = new()
    {
        EntityTypes = ["event"],
        Domains = [new SelectorDomain { Key = "style-fashion", Except = [Beauty] }],
        Topics = ["fashion-show"],
    };

    /// <summary>Gets the registry that creates each case's isolated tenant and container.</summary>
    protected abstract IRegistrationBroker Registry { get; }
    /// <summary>Gets the post implementation under test.</summary>
    protected abstract IPostBroker Posts { get; }
    /// <summary>Gets the preview implementation under test.</summary>
    protected abstract ISubscriptionPreviewBroker Preview { get; }
    /// <summary>Gets the implementation's clock.</summary>
    protected abstract TimeProvider Clock { get; }

    /// <summary>A preview counts the window's posts by verdict, by source and by clause, and names the matches and the
    /// refused posts one relaxation would admit.</summary>
    [Fact]
    public async Task Preview_ShouldCountMatchesPendingAndClauses()
    {
        var context = await ContextAsync();
        var otherSource = await SourceAsync(context, "other");
        var group = Value(await Registry.DeclareSourceGroupAsync(new SourceGroupDeclaration { ContainerId = context.Container, Name = "Publisher A" }));
        Value(await Registry.UpdateSourceAsync(context.Source, new SourceUpdate { Name = "Source A", GroupId = group.Id }));
        var fashion = await SaveAsync(context, context.Source, "high-fashion", "High fashion event", ["type:event", "domain:style-fashion/high-fashion", "domain:style-fashion"], "Tashkent Fashion Week");
        await SaveAsync(context, context.Source, "beauty-expo", "Beauty expo", ["type:event", "domain:" + Beauty, "domain:style-fashion", "topic:cosmetics"]);
        var show = await SaveAsync(context, context.Source, "beauty-show", "Beauty show", ["type:event", "domain:" + Beauty, "domain:style-fashion", "topic:cosmetics", "topic:fashion-show"]);
        await SaveAsync(context, context.Source, "listing", "A listing", ["type:listing", "domain:style-fashion/high-fashion", "domain:style-fashion"]);
        await SaveAsync(context, otherSource, "marathon", "A marathon", ["type:event", "domain:sports", "topic:marathon"]);
        await SaveAsync(context, otherSource, "untagged", "A fresh post with no answer yet", []);

        var preview = Value(await Preview.PreviewAsync(Request(context, Fashion)));

        preview.SinceUtc.Should().BeCloseTo(Clock.GetUtcNow().AddDays(-SelectorRules.DefaultWindowDays), TimeSpan.FromMinutes(5));
        (preview.Considered, preview.Tagged, preview.Matched, preview.NoMatch, preview.Pending).Should().Be((6, 5, 2, 3, 1));
        preview.Truncated.Should().BeFalse();
        preview.Sources.Should().Equal(
            new PreviewSourceCount { SourceId = context.Source, Name = "Source A", GroupId = group.Id, Considered = 4, Matched = 2, Pending = 0 },
            new PreviewSourceCount { SourceId = otherSource, Considered = 2, Matched = 0, Pending = 1 });
        preview.Clauses.Should().Equal(
            new PreviewClauseCount { Clause = "type", Removes = 1 },
            new PreviewClauseCount { Clause = "domain:style-fashion", Adds = 1 },
            new PreviewClauseCount { Clause = "topic:fashion-show", Adds = 1 },
            new PreviewClauseCount { Clause = "except:" + Beauty, Removes = 1 });
        preview.Samples.Should().HaveCount(2);
        preview.Samples.Should().OnlyContain(post => post.Verdict == SelectorVerdict.Match && post.Revision == 1);
        var sampledFashion = preview.Samples.Single(post => post.PostId == fashion);
        sampledFashion.Title.Should().Be("High fashion event");
        sampledFashion.TitleEn.Should().Be("Tashkent Fashion Week");
        sampledFashion.Clauses.Should().Equal("type:event", "domain:style-fashion");
        preview.Samples.Single(post => post.PostId == show).Clauses.Should().Equal("type:event", "topic:fashion-show");
        preview.NearMisses.Should().HaveCount(2);
        preview.NearMisses.Should().OnlyContain(post => post.Verdict == SelectorVerdict.NoMatch);
        preview.NearMisses.Single(post => post.Title == "A listing").Clauses.Should().Equal("type");
        preview.NearMisses.Single(post => post.Title == "Beauty expo").Clauses.Should().Equal("except:" + Beauty);

        Value(await Preview.PreviewAsync(Request(context, Fashion) with { Samples = 0 })).Samples.Should().BeEmpty();
        var limited = Value(await Preview.PreviewAsync(Request(context, Fashion) with { Samples = 1 }));
        limited.Samples.Should().ContainSingle();
        limited.Matched.Should().Be(2);

        var groupOnly = Value(await Preview.PreviewAsync(Request(context, Fashion with
        {
            Sources = new SelectorSources { Include = [new SelectorSourceRef { GroupId = group.Id }] },
        })));
        (groupOnly.Considered, groupOnly.Matched, groupOnly.NoMatch, groupOnly.Pending).Should().Be((6, 2, 4, 0));
        groupOnly.Sources.Should().ContainSingle().Which.SourceId.Should().Be(context.Source);
    }

    /// <summary>A preview refuses another tenant's container, a window over a year or in the future, a sample count out of
    /// range and a malformed selector, and answers an empty window with zeros.</summary>
    [Fact]
    public async Task Preview_ShouldRefuseForeignContainersAndBadWindows()
    {
        var context = await ContextAsync();
        var other = Value(await Registry.RegisterTenantAsync(new TenantRegistration { Slug = "other-" + context.Unique, Name = "Other" }));
        var foreign = Value(await Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = other.Id }));
        var now = Clock.GetUtcNow();

        Error(await Preview.PreviewAsync(Request(context, Fashion) with { ContainerId = foreign.Id })).Type.Should().Be(AppErrorType.NotFound);
        Error(await Preview.PreviewAsync(Request(context, Fashion) with { ContainerId = Guid.NewGuid() })).Type.Should().Be(AppErrorType.NotFound);
        Error(await Preview.PreviewAsync(Request(context, Fashion) with { TenantId = Guid.NewGuid() })).Type.Should().Be(AppErrorType.NotFound);
        Error(await Preview.PreviewAsync(Request(context, Fashion) with { SinceUtc = now.AddDays(-SelectorRules.MaxWindowDays).AddMinutes(-10) })).Type.Should().Be(AppErrorType.Validation);
        Error(await Preview.PreviewAsync(Request(context, Fashion) with { SinceUtc = now.AddMinutes(10) })).Type.Should().Be(AppErrorType.Validation);
        Error(await Preview.PreviewAsync(Request(context, Fashion) with { Samples = 51 })).Type.Should().Be(AppErrorType.Validation);
        Error(await Preview.PreviewAsync(Request(context, Fashion) with { Samples = -1 })).Type.Should().Be(AppErrorType.Validation);
        Error(await Preview.PreviewAsync(Request(context, new SubscriptionSelector { EntityTypes = ["concert"] }))).Type.Should().Be(AppErrorType.Validation);
        Error(await Preview.PreviewAsync(Request(context, Fashion with
        {
            Sources = new SelectorSources { Include = [new SelectorSourceRef { SourceId = Guid.NewGuid() }] },
        }))).Type.Should().Be(AppErrorType.Validation);

        var empty = Value(await Preview.PreviewAsync(Request(context, Fashion)));
        (empty.Considered, empty.Tagged, empty.Matched, empty.NoMatch, empty.Pending).Should().Be((0, 0, 0, 0, 0));
        empty.Sources.Should().BeEmpty();
        empty.Samples.Should().BeEmpty();
        empty.NearMisses.Should().BeEmpty();
    }

    private async Task<Context> ContextAsync()
    {
        var unique = Guid.NewGuid().ToString("N");
        var tenant = Value(await Registry.RegisterTenantAsync(new TenantRegistration { Slug = "preview-" + unique, Name = "Preview conformance" }));
        var container = Value(await Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id }));
        var source = Value(await Registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container.Id, Family = SourceFamily.Site, Handle = unique + ".example", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        }));
        return new Context(unique, tenant.Id, container.Id, source.Id);
    }

    private async Task<Guid> SourceAsync(Context context, string name) =>
        Value(await Registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = context.Container, Family = SourceFamily.Site, Handle = $"{name}.{context.Unique}.example", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        })).Id;

    /// <summary>Saves one capture whose envelope carries the Layer 1 tags, as a resolved answer would, and returns its post.</summary>
    private async Task<Guid> SaveAsync(Context context, Guid source, string key, string title, string[] tags, string? titleEn = null)
    {
        var capture = new Capture
        {
            ExternalKey = key, Url = $"https://fixture.example/{key}", RequestUrl = $"https://fixture.example/{key}", FinalUrl = $"https://fixture.example/{key}",
            CapturedAtUtc = Clock.GetUtcNow().ToUniversalTime(), Body = Encoding.UTF8.GetBytes($"{title} {key}"), ContentProjection = $"{title} {key}",
            ContentType = "text/html", HttpStatus = 200,
        };
        var saved = Value(await Posts.SaveCaptureAsync(context.Container, source, null, capture, new PostEnvelope
        {
            Kind = PostKind.Event, Title = title, Text = capture.ContentProjection,
            Tags = [.. tags.Select(tag => new PostTag { Tag = tag, Tier = ExtractionTier.SmallModel })],
            Extras = titleEn is null ? new Dictionary<string, JsonElement>()
                : new Dictionary<string, JsonElement> { ["postTags"] = JsonSerializer.SerializeToElement(new { titleEn }) },
            Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
        }));
        return saved.Post.Id;
    }

    private static SubscriptionPreviewRequest Request(Context context, SubscriptionSelector selector) =>
        new() { TenantId = context.Tenant, ContainerId = context.Container, Selector = selector };

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
    private static AppError Error<T>(Result<T> result) where T : notnull => result is Result<T>.Failure failure ? failure.Error : throw new Xunit.Sdk.XunitException("Expected failure.");
    private sealed record Context(string Unique, Guid Tenant, Guid Container, Guid Source);
}
