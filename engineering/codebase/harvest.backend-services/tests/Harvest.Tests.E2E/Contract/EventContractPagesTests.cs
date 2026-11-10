using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Harvest.Tests.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

namespace Harvest.Tests.E2E.Contract;

/// <summary>Renders SCFD's golden <c>wow2.event.v1</c> pull pages through the real HTTP host over a fresh database and
/// pins them in <c>tests/Contract/wow2.event.v1/</c>, which Events replays.</summary>
/// <remarks>With <c>HARVEST_WRITE_CONTRACT=1</c> the test writes the files and the manifest; otherwise each rendered
/// file must equal its checked-in copy byte for byte. Pages chain: each pull passes the previous page's cursor.</remarks>
public sealed class EventContractPagesTests
{
    private const string WriteSwitch = "HARVEST_WRITE_CONTRACT";
    private const string Schema = "wow2.event.v1";
    private const string SourceHost = "afisha.example.test";
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Pages_ShouldEqualTheCheckedInContract()
    {
        string database = (await PostgresServer.SharedAsync()).NewDatabase("contract");
        await using var host = HarvestApiFixture.CreateHost(database);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, HarvestApiFixture.OperatorKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var clock = host.Clock;
        clock.SetUtcNow(Start);
        DateTimeOffset Tick()
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            return clock.GetUtcNow();
        }

        var files = new List<ContractFile>();
        var wire = host.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var normalizer = new ContractNormalizer(wire);

        // The subscription every page comes from: the event schema, no filter, live from now on.
        string tenantId = await CreateAsync(client, "/api/tenants", new { slug = "events-contract", name = "Events contract" });
        string containerId = await CreateAsync(client, "/api/containers", new { kind = "private", tenantId });
        string sourceId = await CreateAsync(client, "/api/sources", new
        {
            containerId, family = "site", handle = SourceHost, coverage = "whole", completeness = "complete", posture = "ownFetch",
        });
        string pipelineId = await CreateAsync(client, "/api/pipelines", new { containerId, sourceId });
        Tick();
        string subscriptionId = await CreateAsync(client, "/api/subscriptions", new
        {
            tenantId, containerId, pipelineId, backfill = "none", schema = Schema,
            context = new Dictionary<string, string> { ["catalog"] = "events" },
        });
        var subscription = await GetAsync(client, $"/api/subscriptions/{subscriptionId}");
        Data(subscription).GetProperty("createdAtUtc").GetDateTimeOffset().Should().Be(Start.AddMinutes(1), "the module must read the fixed clock");
        files.Add(new("subscription.json", ["subscription"], normalizer.Normalize(subscription)));

        var container = Guid.Parse(containerId);
        var source = Guid.Parse(sourceId);
        await using var scope = host.Services.CreateAsyncScope();
        var posts = scope.ServiceProvider.GetRequiredService<IPostBroker>();
        var extractor = scope.ServiceProvider.GetRequiredService<IPostExtractor>();
        extractor.Should().BeOfType<RulesPostExtractor>();

        async Task<PostModel> SaveAsync(Capture capture, Func<PostEnvelope, PostEnvelope>? classify = null)
        {
            var envelope = (await extractor.ExtractAsync(capture, new ExtractionRequest { Family = SourceFamily.Site })).ValueOrThrow();
            var saved = (await posts.SaveCaptureAsync(container, source, null, capture, classify?.Invoke(envelope) ?? envelope)).ValueOrThrow();
            return saved.Post;
        }

        async Task<(byte[] Bytes, JsonElement Page)> PullAsync(string? cursor, int expected)
        {
            var path = $"/api/subscriptions/{subscriptionId}/events" + (cursor is null ? string.Empty : "?cursor=" + Uri.EscapeDataString(cursor));
            for (var attempt = 0; ; attempt++)
            {
                // A row is readable once no older transaction runs on the shared server, so a page may lag its writes.
                var bytes = await GetAsync(client, path);
                var page = Data(bytes);
                var count = page.GetProperty("data").GetArrayLength();
                if (count == expected || attempt == 100)
                {
                    count.Should().Be(expected, Encoding.UTF8.GetString(bytes));
                    return (bytes, page);
                }
                await Task.Delay(100);
            }
        }

        // 01: a rich event, new.
        var rich = await SaveAsync(Rich(Tick(), "150000"));
        var (newBytes, newPage) = await PullAsync(null, 1);
        var created = Events(newPage).Single();
        created.GetProperty("type").GetString().Should().Be(DeliveryEventTypes.PostNew);
        var createdData = created.GetProperty("data");
        createdData.GetProperty("classification").ValueKind.Should().Be(JsonValueKind.Null);
        createdData.GetProperty("starts").GetProperty("precision").GetString().Should().Be("second");
        createdData.GetProperty("ends").GetProperty("precision").GetString().Should().Be("day");
        MentionKinds(createdData).Should().Equal("venue", "city", "street");
        createdData.GetProperty("place").GetProperty("geo").ValueKind.Should().Be(JsonValueKind.Object);
        foreach (var field in new[] { "postedAt", "mapping", "capture", "schemaOrg" })
            createdData.GetProperty(field).ValueKind.Should().Be(JsonValueKind.Object, field);
        foreach (var field in new[] { "offers", "links", "contentLinks", "media" })
            createdData.GetProperty(field).GetArrayLength().Should().BePositive(field);
        files.Add(new("01-new.json", ["post.new"], normalizer.Normalize(newBytes)));

        // 03: the same post at revision 2, its ticket price changed.
        await SaveAsync(Rich(Tick(), "180000"));
        var (changedBytes, changedPage) = await PullAsync(Next(newPage), 1);
        var changed = Events(changedPage).Single();
        changed.GetProperty("type").GetString().Should().Be(DeliveryEventTypes.PostChanged);
        changed.GetProperty("revision").GetInt32().Should().Be(2);
        changed.GetProperty("postId").GetGuid().Should().Be(rich.Id);
        files.Add(new("03-changed.json", ["post.changed"], normalizer.Normalize(changedBytes)));

        // 04: nothing new; the page returns the cursor the request sent.
        var (emptyBytes, emptyPage) = await PullAsync(Next(changedPage), 0);
        Next(emptyPage).Should().Be(Next(changedPage));
        emptyPage.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        files.Add(new("04-empty.json", ["empty"], normalizer.Normalize(emptyBytes)));

        // 05: lifecycle through the real broker — a duplicate listing, the link gone and relisted, merge and unmerge.
        // post.relisted, pipeline.run and pipeline.health have no producer yet, so no page can carry them.
        var mirror = await SaveAsync(Mirror(Tick()));
        Tick();
        (await posts.ObserveLinkAsync(container, source, new LinkObservation
        {
            ExternalKey = EventUrl("jazz-night"), Signal = LinkSignal.StrongNegative, Reason = "direct_410", HttpStatus = 410,
        })).ValueOrThrow().State.Should().Be(PostState.Gone);
        Tick();
        (await posts.ObserveLinkAsync(container, source, new LinkObservation
        {
            ExternalKey = EventUrl("jazz-night"), Signal = LinkSignal.Present, Reason = "seen_again", HttpStatus = 200,
        })).ValueOrThrow().State.Should().Be(PostState.Live);
        Tick();
        var merge = (await posts.MergeAsync(container, rich.Id, mirror.Id)).ValueOrThrow();
        Tick();
        (await posts.UnmergeAsync(container, merge.Id)).ValueOrThrow().Undone.Should().BeTrue();
        var (lifecycleBytes, lifecyclePage) = await PullAsync(Next(emptyPage), 7);
        Events(lifecyclePage).Select(item => item.GetProperty("type").GetString()).Should().Equal(
            DeliveryEventTypes.PostNew, DeliveryEventTypes.LinkState, DeliveryEventTypes.LinkState, DeliveryEventTypes.PostMerged,
            DeliveryEventTypes.PostChanged, DeliveryEventTypes.PostUnmerged, DeliveryEventTypes.PostUnmerged);
        var merged = Events(lifecyclePage)[3];
        merged.GetProperty("postId").GetGuid().Should().Be(mirror.Id);
        merged.GetProperty("data").GetProperty("mergedInto").GetGuid().Should().Be(rich.Id);
        Events(lifecyclePage)[4].GetProperty("postId").GetGuid().Should().Be(rich.Id);
        files.Add(new("05-lifecycle.json",
            ["post.new", "link.state:gone", "link.state:relisted", "post.merged", "post.changed:survivor", "post.unmerged:survivor", "post.unmerged:loser"],
            normalizer.Normalize(lifecycleBytes)));

        // 06: classification, set from the test because no production step writes it yet.
        var verdicts = new (string Slug, string Title, PostClassification Classification)[]
        {
            ("runway-main", "Tashkent Fashion Week: показ коллекции весна-лето", Classification(ClassificationVerdict.Relevant, 0.9, "A designer collection shown on a runway.", "runway")),
            ("book-fair", "Книжная ярмарка в Национальной библиотеке", Classification(ClassificationVerdict.Irrelevant, 0.95, "A book fair, not a fashion event.", null)),
            ("open-day", "День открытых дверей в колледже дизайна", Classification(ClassificationVerdict.Uncertain, 0.5, "A design school open day; a show is not announced.", null)),
            ("textile-expo", "Выставка текстиля с показом тканей", Classification(ClassificationVerdict.Relevant, 0.3, "A textile fair that may include a runway segment.", "runway")),
        };
        foreach (var (slug, title, classification) in verdicts)
            await SaveAsync(Simple(Tick(), slug, title), envelope => envelope with { Classification = classification });
        var (classifiedBytes, classifiedPage) = await PullAsync(Next(lifecyclePage), 4);
        Events(classifiedPage).Select(item => item.GetProperty("data").GetProperty("classification").GetProperty("verdict").GetString())
            .Should().Equal("relevant", "irrelevant", "uncertain", "relevant");
        Events(classifiedPage).Select(item => item.GetProperty("data").GetProperty("kind").GetString())
            .Should().Equal("runway", null, null, "runway");
        files.Add(new("06-classification.json", ["relevant:runway:0.9", "irrelevant", "uncertain", "relevant:0.3"], normalizer.Normalize(classifiedBytes)));

        // 07: an afisha place before and after the city fix, through the real RulesPostExtractor.
        await SaveAsync(Afisha(Tick(), "youth-palace-before", "Вечер камерной музыки", """
            {"@type":"Place","name":"Дворец творчества молодёжи","address":"Ташкент, просп. Мустакиллик, 2","geo":{"@type":"GeoCoordinates","latitude":"41.3153000","longitude":"69.2727000"}}
            """));
        await SaveAsync(Afisha(Tick(), "youth-palace-after", "Концерт молодёжного оркестра", """
            {"@type":"Place","name":"Дворец творчества молодёжи","address":{"@type":"PostalAddress","addressLocality":"Ташкент","streetAddress":"просп. Мустакиллик, 2"},"geo":{"@type":"GeoCoordinates","latitude":"41.3153000","longitude":"69.2727000"}}
            """));
        var (placeBytes, placePage) = await PullAsync(Next(classifiedPage), 2);
        MentionKinds(Events(placePage)[0].GetProperty("data")).Should().Equal("venue", "address");
        MentionKinds(Events(placePage)[1].GetProperty("data")).Should().Equal("venue", "city", "street");
        files.Add(new("07-afisha-place.json", ["place:before-city-fix", "place:after-city-fix"], normalizer.Normalize(placeBytes)));

        // 08: post.purged is reserved and no step emits it, so the row goes in with the writer's columns and rule.
        var current = (await posts.GetPostAsync(container, rich.Id)).ValueOrThrow();
        await PurgeAsync(database, Guid.Parse(subscriptionId), rich.Id, current.Revision + 1, Tick());
        var (purgedBytes, purgedPage) = await PullAsync(Next(placePage), 1);
        var purged = Events(purgedPage).Single();
        purged.GetProperty("type").GetString().Should().Be(DeliveryEventTypes.PostPurged);
        purged.TryGetProperty("data", out _).Should().BeFalse("a row without a payload serves no data");
        files.Add(new("08-purged.json", ["post.purged"], normalizer.Normalize(purgedBytes)));

        files.Add(new("manifest.json", [], Manifest(files)));
        ContractFolder.WriteOrVerify(files, Environment.GetEnvironmentVariable(WriteSwitch) == "1");
    }

    private static Capture Rich(DateTimeOffset at, string price) => EventCapture(at, "jazz-night", """
        {"@context":"https://schema.org","@type":"Event","name":"Вечер живого джаза","description":"Квартет играет стандарты и авторские пьесы. Справки: +998 71 205 14 14.","startDate":"2026-12-05T19:00:00+05:00","endDate":"2026-12-06","inLanguage":"ru","location":{"@type":"Place","name":"Дворец Истиклол","address":{"@type":"PostalAddress","addressLocality":"Ташкент","streetAddress":"ул. Амира Темура, 107"},"geo":{"@type":"GeoCoordinates","latitude":41.3139,"longitude":69.2803}},"organizer":{"@type":"Organization","name":"Tashkent Jazz Club","url":"https://jazz.example.test/"},"offers":{"@type":"Offer","price":"PRICE","priceCurrency":"UZS","url":"https://HOST/events/jazz-night/tickets"},"image":"https://HOST/media/jazz-night.jpg"}
        """.Replace("PRICE", price).Replace("HOST", SourceHost), published: Start.AddDays(-2), links: [new() { Url = $"https://{SourceHost}/venues/istiqlol", Text = "Дворец Истиклол" }]);

    private static Capture Mirror(DateTimeOffset at) => EventCapture(at, "jazz-night-copy", """
        {"@context":"https://schema.org","@type":"Event","name":"Вечер живого джаза (копия анонса)","description":"Повтор анонса на другой странице.","startDate":"2026-12-05T19:00:00+05:00","location":{"@type":"Place","name":"Дворец Истиклол"}}
        """);

    private static Capture Simple(DateTimeOffset at, string slug, string title) => EventCapture(at, slug, """
        {"@context":"https://schema.org","@type":"Event","name":"TITLE","description":"TITLE. Вход по регистрации.","startDate":"2026-12-12","location":{"@type":"Place","name":"Ташкент"}}
        """.Replace("TITLE", title));

    private static Capture Afisha(DateTimeOffset at, string slug, string title, string location) => EventCapture(at, slug, """
        {"@context":"https://schema.org","@type":"Event","name":"TITLE","startDate":"2026-12-19T18:30:00+05:00","location":LOCATION}
        """.Replace("TITLE", title).Replace("LOCATION", location.Trim()));

    private static Capture EventCapture(DateTimeOffset at, string slug, string schemaOrg, DateTimeOffset? published = null, IReadOnlyList<CaptureLink>? links = null)
    {
        var node = JsonDocument.Parse(schemaOrg).RootElement.Clone();
        var projection = node.GetProperty("name").GetString() + "\n" + schemaOrg.Trim();
        return new Capture
        {
            ExternalKey = EventUrl(slug), Url = EventUrl(slug), RequestUrl = EventUrl(slug), FinalUrl = EventUrl(slug),
            HttpStatus = 200, CapturedAtUtc = at, ContentType = "text/html; charset=utf-8", Locale = "ru", PublishedAtUtc = published,
            Body = Encoding.UTF8.GetBytes($"<script type=\"application/ld+json\">{schemaOrg.Trim()}</script>"),
            ContentProjection = projection, Structured = [node], Links = links ?? [],
        };
    }

    private static string EventUrl(string slug) => $"https://{SourceHost}/events/{slug}";

    private static PostClassification Classification(ClassificationVerdict verdict, double confidence, string reason, string? kind) =>
        new() { Classifier = "model:contract@1", Verdict = verdict, Confidence = confidence, Reason = reason, Kind = kind };

    private static async Task PurgeAsync(string database, Guid subscription, Guid post, int revision, DateTimeOffset at)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO harvest_subscription_event(subscription_id,event_id,item_id,type,post_id,post_key,revision,occurred_at)
            VALUES(@subscription,@event,@item,@type,@post,@key,@revision,@at)
            """;
        command.Parameters.AddWithValue("subscription", subscription);
        command.Parameters.AddWithValue("event", Guid.CreateVersion7(at));
        command.Parameters.AddWithValue("item", ContractNormalizer.ItemId(post, revision, DeliveryEventTypes.PostPurged));
        command.Parameters.AddWithValue("type", DeliveryEventTypes.PostPurged);
        command.Parameters.AddWithValue("post", post);
        command.Parameters.AddWithValue("key", post.ToString("N"));
        command.Parameters.AddWithValue("revision", revision);
        command.Parameters.AddWithValue("at", at);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private static byte[] Manifest(IReadOnlyList<ContractFile> files)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            writer.WriteStartArray("files");
            foreach (var file in files)
            {
                writer.WriteStartObject();
                writer.WriteString("name", file.Name);
                writer.WriteString("sha256", Convert.ToHexString(SHA256.HashData(file.Bytes)).ToLowerInvariant());
                writer.WriteStartArray("cases");
                foreach (var name in file.Cases) writer.WriteStringValue(name);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static async Task<string> CreateAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsync(path, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }

    private static async Task<byte[]> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, Encoding.UTF8.GetString(bytes));
        return bytes;
    }

    private static JsonElement Data(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        return json.RootElement.GetProperty("data").Clone();
    }

    private static JsonElement[] Events(JsonElement page) => page.GetProperty("data").EnumerateArray().ToArray();

    private static string Next(JsonElement page) => page.GetProperty("next").GetString()!;

    private static string?[] MentionKinds(JsonElement data) =>
        data.GetProperty("place").GetProperty("mentions").EnumerateArray().Select(mention => mention.GetProperty("kind").GetString()).ToArray();
}

/// <summary>Holds one contract file: its name, the cases it carries and its stable bytes.</summary>
internal sealed record ContractFile(string Name, IReadOnlyList<string> Cases, byte[] Bytes);

/// <summary>Writes or verifies <c>tests/Contract/wow2.event.v1/</c>, found above the test assembly.</summary>
internal static class ContractFolder
{
    /// <summary>Writes the files when <paramref name="write"/> is set; otherwise fails on the first byte that differs.</summary>
    public static void WriteOrVerify(IReadOnlyList<ContractFile> files, bool write)
    {
        var folder = Locate();
        if (write) Directory.CreateDirectory(folder);
        foreach (var file in files)
        {
            var path = Path.Combine(folder, file.Name);
            if (write)
            {
                File.WriteAllBytes(path, file.Bytes);
                continue;
            }
            File.Exists(path).Should().BeTrue($"{file.Name} is missing; regenerate with HARVEST_WRITE_CONTRACT=1");
            var expected = File.ReadAllBytes(path);
            if (expected.AsSpan().SequenceEqual(file.Bytes)) continue;
            var at = 0;
            while (at < expected.Length && at < file.Bytes.Length && expected[at] == file.Bytes[at]) at++;
            string Around(byte[] bytes) => Encoding.UTF8.GetString(bytes, Math.Max(0, at - 80), Math.Min(bytes.Length, at + 80) - Math.Max(0, at - 80));
            throw new Xunit.Sdk.XunitException(
                $"{file.Name} drifted from the contract at byte {at}.\nchecked in: …{Around(expected)}…\nrendered:   …{Around(file.Bytes)}…\n" +
                "Change the wire deliberately, then regenerate with HARVEST_WRITE_CONTRACT=1.");
        }
    }

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SCFD.BackendServices.slnx")))
                return Path.Combine(directory.FullName, "tests", "Contract", "wow2.event.v1");
        throw new InvalidOperationException("SCFD.BackendServices.slnx was not found above the test assembly.");
    }
}
