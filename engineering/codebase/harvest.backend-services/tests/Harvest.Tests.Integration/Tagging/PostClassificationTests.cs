using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Harvest.Tests.Integration.Posts;
using Harvest.Tests.Integration.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace Harvest.Tests.Integration.Tagging;

/// <summary>The classification the tags give (S16) and the estimated dates an event answer gives (S17), written by the
/// post store over PostgreSQL with the real AI runner and a scripted caller.</summary>
public sealed class PostClassificationTests
{
    private const string Title = "Tashkent Fashion Week";
    private const string Text = "Moda haftaligi dekabrda Uzexpocentre'da bo'ladi.";

    [Fact]
    public async Task Tags_ShouldWriteTheVerdictInTheirOwnRevision_AndTheEventAnswerItsKindAndAnEstimatedStart()
    {
        await using var host = await PostExtractionTests.StartAsync(PostExtractionTests.Want("event", "style-fashion"));
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = host.PipelineId,
        }));
        var saved = await host.SaveAsync("fw", Title, Text);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(domains: [new { node = "style-fashion/womens-fashion", primary = true }])));

        (await host.Tags.ProcessDueAsync()).Should().Be(1);

        var tagged = PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, saved.Post.Id));
        tagged.Revision.Should().Be(2);
        tagged.Base.Classification.Should().BeEquivalentTo(new PostClassification
        {
            Classifier = "model:anthropic/claude-haiku-5-5@post-tags:1", Verdict = ClassificationVerdict.Relevant, Confidence = 0.9,
            Reason = "event under style-fashion", Kind = "event",
        });
        (await host.ScalarAsync<string[]>("SELECT changed FROM harvest_post_revisions WHERE post_id=@post AND revision=2", ("post", tagged.Id)))
            .Should().Equal("classification", "extras", "tags");
        (await EventsAsync(host, subscription.Id)).Should().Be("post.new:1,post.changed:2", "the tags and their verdict are one revision");
        EventSchemaMapper.Map(tagged.Base)!.Value.GetProperty("starts").ValueKind.Should().Be(JsonValueKind.Null);

        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(PostExtractionTests.EventAnswer(
            occurrences: [new { starts = "2026-12", ends = (string?)null, precision = "month", certainty = "estimated", evidence = "dekabrda" }])));
        var extraction = PostExtractionTests.Extraction(host);

        (await extraction.ProcessDueAsync()).Should().Be(1);

        var estimated = PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, saved.Post.Id));
        estimated.Revision.Should().Be(3);
        estimated.Base.Classification!.Kind.Should().Be("runway", "a done extract-event answer names the format");
        estimated.Base.Dates.Should().ContainSingle().Which.Should().Be(new PostDate
        {
            Role = DateRole.Start, Value = "2026-12", Precision = DatePrecision.Month, Raw = "dekabrda", Certainty = DateCertainty.Estimated,
        });
        var starts = EventSchemaMapper.Map(estimated.Base)!.Value.GetProperty("starts");
        starts.GetProperty("certainty").GetString().Should().Be("estimated");
        starts.GetProperty("value").GetString().Should().Be("2026-12");
        (await EventsAsync(host, subscription.Id)).Should().Be("post.new:1,post.changed:2,post.changed:3");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_work WHERE post_id=@post", ("post", saved.Post.Id)))
            .Should().Be(0, "the revision kept its content hash, so nothing queues again");

        host.Clock.Advance(TimeSpan.FromHours(1));
        var stated = await SaveDatedAsync(host, "fw", Title, Text, "2026-12-12");

        stated.Post.Revision.Should().Be(4);
        stated.Post.Base.Dates.Should().ContainSingle().Which.Certainty.Should().BeNull("a stated date carries no certainty value");
        starts = EventSchemaMapper.Map(stated.Post.Base)!.Value.GetProperty("starts");
        starts.GetProperty("value").GetString().Should().Be("2026-12-12");
        starts.GetProperty("certainty").GetString().Should().Be("stated", "a later stated date replaces the estimate");
        stated.Post.Base.Classification!.Kind.Should().Be("runway", "the title and text did not change, so the answers still apply");
        (await EventsAsync(host, subscription.Id)).Should().Be("post.new:1,post.changed:2,post.changed:3,post.changed:4");
    }

    [Fact]
    public async Task Tags_ShouldGiveAnUncertainVerdict_ForAnUnsureEntityTypeOrPrimaryDomain()
    {
        await using var host = await PostExtractionTests.StartAsync(PostExtractionTests.Want("event", "style-fashion"));
        var unsure = await host.SaveAsync("unsure", "A gathering", "Bir tadbir bo'ladi.");
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        var unknown = await host.SaveAsync("unknown", "A show", "Ko'rgazma bo'ladi.");
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        var none = await host.SaveAsync("none", "Opinion", "Fikr.");
        host.Caller.Enqueue(
            ScriptedAiModelCaller.Answer(TagsHost.Answer(entityType: "unsure")),
            ScriptedAiModelCaller.Answer(TagsHost.Answer(domains: [new { node = "made-up-node", primary = true }])),
            ScriptedAiModelCaller.Answer(TagsHost.Answer(entityType: "none")));

        (await host.Tags.ProcessDueAsync()).Should().Be(3);

        async Task<PostClassification> VerdictAsync(Guid id) =>
            PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, id)).Base.Classification!;
        (await VerdictAsync(unsure.Post.Id)).Should().Match<PostClassification>(value =>
            value.Verdict == ClassificationVerdict.Uncertain && value.Reason == PostTagsClassification.EntityTypeUnsure && value.Kind == null);
        (await VerdictAsync(unknown.Post.Id)).Should().Match<PostClassification>(value =>
            value.Verdict == ClassificationVerdict.Uncertain && value.Reason == PostTagsClassification.DomainUnsure && value.Kind == "event");
        (await VerdictAsync(none.Post.Id)).Should().Match<PostClassification>(value =>
            value.Verdict == ClassificationVerdict.Irrelevant && value.Kind == "none");
    }

    [Fact]
    public async Task TaggingOff_ShouldLeaveTheRulesOnlyPostExactlyAsAHostWithoutAi()
    {
        await using var off = await PostExtractionTests.StartAsync(enabled: false, PostExtractionTests.Want("event", "style-fashion"));
        await using var plain = await TagsHost.StartAsync(ai: false);

        var withAi = await SaveDatedAsync(off, "fw", Title, Text, null);
        var without = await SaveDatedAsync(plain, "fw", Title, Text, null);

        withAi.Post.Revision.Should().Be(1);
        withAi.Post.Base.Classification.Should().BeNull();
        withAi.Post.Base.Tags.Should().BeEmpty();
        JsonSerializer.Serialize(withAi.Post.Base, JsonOptionsConstants.Default)
            .Should().Be(JsonSerializer.Serialize(without.Post.Base, JsonOptionsConstants.Default));
        (await off.ScalarAsync<long>("SELECT count(*) FROM harvest_post_work")).Should().Be(0);
        (await off.ScalarAsync<long>("SELECT count(*) FROM harvest_post_tags")).Should().Be(0);
        off.Caller.Requests.Should().BeEmpty();
    }

    /// <summary>Saves a capture whose envelope states <paramref name="start"/>, when given; the projection carries it, so
    /// a new date is a new capture while the title and text, and so the answers, stay.</summary>
    private static async Task<CaptureSaveResult> SaveDatedAsync(TagsHost host, string key, string title, string text, string? start)
    {
        var projection = $"{title}\n{text}\n{start}";
        var capture = new Capture
        {
            ExternalKey = key, Url = $"https://mstar.uz/{key}", RequestUrl = $"https://mstar.uz/{key}", FinalUrl = $"https://mstar.uz/{key}",
            CapturedAtUtc = host.Clock.GetUtcNow(), Body = Encoding.UTF8.GetBytes(projection), ContentProjection = projection,
            HttpStatus = 200, ContentType = "text/html",
        };
        var envelope = PostStoreTests.Envelope(title, text) with
        {
            Dates = start is null ? [] : [new PostDate { Role = DateRole.Start, Value = start, Precision = DatePrecision.Day, Raw = start }],
        };
        return PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture, envelope));
    }

    /// <summary>Lists a subscription's events in append order: event ids share the fixed clock's millisecond.</summary>
    private static Task<string> EventsAsync(TagsHost host, Guid subscription) => host.ScalarAsync<string>("""
        SELECT string_agg(type || ':' || revision, ',' ORDER BY id) FROM harvest_subscription_event WHERE subscription_id=@subscription
        """, ("subscription", subscription));
}
