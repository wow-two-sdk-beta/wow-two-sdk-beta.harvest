using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Harvest.Tests.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using Capture = WoW.Two.Sdk.Beta.Be.Harvest.Readers.Capture;

namespace Harvest.Tests.E2E.Contract;

/// <summary>Renders SCFD's golden selector pull pages through the real HTTP host over a fresh database and pins them in
/// <c>tests/Contract/wow2.event.v1/selector/</c>, which Events replays beside the channel pages.</summary>
/// <remarks>With <c>HARVEST_WRITE_CONTRACT=1</c> the test writes the files and the manifest; otherwise each rendered file must
/// equal its checked-in copy byte for byte. The pages chain, and the selector change runs in-process on the host's delivery
/// broker, so the pages never depend on a route that only some hosts mount.</remarks>
public sealed class SelectorContractPagesTests
{
    private const string WriteSwitch = "HARVEST_WRITE_CONTRACT";
    private const string Schema = "wow2.event.v1";
    private const string SourceHost = "selector.example.test";
    private const string Name = "ocharo-events";
    private const string Folder = "selector";
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Pages_ShouldEqualTheCheckedInContract()
    {
        string database = (await PostgresServer.SharedAsync()).NewDatabase("selector-contract");
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
        var normalizer = new SelectorContractNormalizer(wire);

        // One container, one group, one source in it: the selector follows the group, and the source belongs to it.
        string tenantId = await CreateAsync(client, "/api/tenants", new { slug = "selector-contract", name = "Selector contract" });
        string containerId = await CreateAsync(client, "/api/containers", new { kind = "private", tenantId });
        string groupId = await PostAsync(client, "/api/sources/groups", new { containerId, name = "Example publisher" }, HttpStatusCode.OK);
        string sourceId = await CreateAsync(client, "/api/sources", new
        {
            containerId, family = "site", handle = SourceHost, coverage = "whole", completeness = "complete", posture = "ownFetch",
        });
        using (var response = await client.PutAsync($"/api/sources/{sourceId}", Json(new { groupId })))
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        // The subscription every page comes from: Ocharo's name, schema and catalog, over style-fashion events of that group,
        // live from now on, as `ocharo setup --selector` creates it in the clauses that matter to the wire.
        Tick();
        string subscriptionId = await CreateAsync(client, "/api/subscriptions", new
        {
            tenantId, containerId, name = Name, schema = Schema, backfill = "none",
            context = new Dictionary<string, string> { ["catalog"] = "ocharo-fashion" },
            selector = new
            {
                entityTypes = new[] { "event" },
                domains = new[] { new { key = "style-fashion" } },
                sources = new { include = new[] { new { groupId, context = new Dictionary<string, string> { ["defaultKind"] = "exhibition" } } } },
            },
        });
        var subscription = Data(await GetAsync(client, $"/api/subscriptions/{subscriptionId}"));
        subscription.GetProperty("createdAtUtc").GetDateTimeOffset().Should().Be(Start.AddMinutes(1), "the module must read the fixed clock");
        subscription.GetProperty("kind").GetString().Should().Be("selector");
        subscription.GetProperty("name").GetString().Should().Be(Name);
        subscription.GetProperty("selectorVersion").GetInt32().Should().Be(1);
        subscription.GetProperty("context").GetProperty("catalog").GetString().Should().Be("ocharo-fashion");
        files.Add(new("subscription.json", ["subscription"], normalizer.Normalize(await GetAsync(client, $"/api/subscriptions/{subscriptionId}"))));

        var container = Guid.Parse(containerId);
        var source = Guid.Parse(sourceId);
        await using var scope = host.Services.CreateAsyncScope();
        var posts = scope.ServiceProvider.GetRequiredService<IPostBroker>();
        var extractor = scope.ServiceProvider.GetRequiredService<IPostExtractor>();
        var delivery = scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();

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

        // 01: a fashion event the model tagged `type:event` and `domain:style-fashion/high-fashion`, with the ancestor the tags
        // overlay adds beside it. It matches the selector's `style-fashion` include, on the most specific node, and the
        // payload carries the group's defaults beside the subscription's own context.
        var capture = Show(Tick());
        var envelope = (await extractor.ExtractAsync(capture, new ExtractionRequest { Family = SourceFamily.Site })).ValueOrThrow();
        var saved = (await posts.SaveCaptureAsync(container, source, null, capture, envelope with
        {
            Kind = PostKind.Event,
            Tags = [Tag("type:event"), Tag("domain:style-fashion"), Tag("domain:style-fashion/high-fashion")],
        })).ValueOrThrow().Post;
        var (matchBytes, matchPage) = await PullAsync(null, 1);
        var matched = Events(matchPage).Single();
        matched.GetProperty("postId").GetGuid().Should().Be(saved.Id);
        matched.GetProperty("type").GetString().Should().Be(DeliveryEventTypes.PostNew);
        matched.GetProperty("reason").GetString().Should().Be("ingest");
        var verdict = matched.GetProperty("match");
        verdict.GetProperty("selectorVersion").GetInt32().Should().Be(1);
        verdict.GetProperty("verdict").GetString().Should().Be("match");
        verdict.GetProperty("clauses").EnumerateArray().Select(clause => clause.GetString()).Should().Equal("type:event", "domain:style-fashion");
        var context = matched.GetProperty("data").GetProperty("context");
        context.GetProperty("catalog").GetString().Should().Be("ocharo-fashion");
        context.GetProperty("defaultKind").GetString().Should().Be("exhibition");
        files.Add(new("01-match.json", ["post.new:match"], normalizer.Normalize(matchBytes)));

        // 02: the selector now excludes the node the post sits under. The change re-judges the post the log holds on the same
        // cursor: it leaves as `post.unmatched`, reason `resync`, under version 2 with the clause that decided it.
        Tick();
        var change = (await delivery.SetSelectorAsync(Guid.Parse(subscriptionId), new SelectorChange
        {
            Selector = new SubscriptionSelector
            {
                EntityTypes = ["event"],
                Domains = [new SelectorDomain { Key = "style-fashion" }],
                ExcludeDomains = ["style-fashion/high-fashion"],
                Sources = new SelectorSources
                {
                    Include = [new SelectorSourceRef { GroupId = Guid.Parse(groupId), Context = new Dictionary<string, string> { ["defaultKind"] = "exhibition" } }],
                },
            },
            ChangedBy = "contract",
        })).ValueOrThrow();
        change.Changed.Should().BeTrue();
        change.Subscription.SelectorVersion.Should().Be(2);
        change.Unmatched.Should().Be(1);
        var (changeBytes, changePage) = await PullAsync(Next(matchPage), 1);
        var unmatched = Events(changePage).Single();
        unmatched.GetProperty("postId").GetGuid().Should().Be(saved.Id);
        unmatched.GetProperty("type").GetString().Should().Be(DeliveryEventTypes.PostUnmatched);
        unmatched.GetProperty("reason").GetString().Should().Be("resync");
        var changed = unmatched.GetProperty("match");
        changed.GetProperty("selectorVersion").GetInt32().Should().Be(2);
        changed.GetProperty("verdict").GetString().Should().Be("noMatch");
        changed.GetProperty("clauses").EnumerateArray().Select(clause => clause.GetString()).Should().Equal("exclude-domain:style-fashion/high-fashion");
        files.Add(new("02-change.json", ["post.unmatched:resync"], normalizer.Normalize(changeBytes)));

        files.Add(new("manifest.json", [], Manifest(files)));
        var write = Environment.GetEnvironmentVariable(WriteSwitch) == "1";
        if (write)
            Directory.CreateDirectory(SelectorFolder.Locate());
        ContractFolder.WriteOrVerify([.. files.Select(file => file with { Name = Folder + "/" + file.Name })], write);
    }

    private static PostTag Tag(string tag) => new() { Tag = tag, Tier = ExtractionTier.SmallModel, Version = "post-tags@3" };

    /// <summary>A schema.org event page of the example source, which the rules extractor reads into an event post.</summary>
    private static Capture Show(DateTimeOffset at)
    {
        const string Slug = "young-designers-show";
        string url = $"https://{SourceHost}/events/{Slug}";
        string schemaOrg = """
            {"@context":"https://schema.org","@type":"Event","name":"Young designers fashion show","description":"Designers show their collections. Entry by registration.","startDate":"2026-12-05T19:00:00+05:00","endDate":"2026-12-06","inLanguage":"en","location":{"@type":"Place","name":"Example exhibition hall","address":{"@type":"PostalAddress","addressLocality":"Tashkent","streetAddress":"Example street, 1"}}}
            """;
        var node = JsonDocument.Parse(schemaOrg).RootElement.Clone();
        return new Capture
        {
            ExternalKey = url, Url = url, RequestUrl = url, FinalUrl = url, HttpStatus = 200, CapturedAtUtc = at,
            ContentType = "text/html; charset=utf-8", Locale = "en", PublishedAtUtc = Start.AddDays(-2),
            Body = Encoding.UTF8.GetBytes($"<script type=\"application/ld+json\">{schemaOrg.Trim()}</script>"),
            ContentProjection = node.GetProperty("name").GetString() + "\n" + schemaOrg.Trim(), Structured = [node], Links = [],
        };
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

    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static Task<string> CreateAsync(HttpClient client, string path, object body) => PostAsync(client, path, body, HttpStatusCode.Created);

    private static async Task<string> PostAsync(HttpClient client, string path, object body, HttpStatusCode expected)
    {
        using var response = await client.PostAsync(path, Json(body));
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, content);
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
}

/// <summary>Finds <c>tests/Contract/wow2.event.v1/selector/</c> above the test assembly.</summary>
internal static class SelectorFolder
{
    /// <summary>Returns the selector folder's path, which may not exist yet.</summary>
    public static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SCFD.BackendServices.slnx")))
                return Path.Combine(directory.FullName, "tests", "Contract", "wow2.event.v1", "selector");
        throw new InvalidOperationException("SCFD.BackendServices.slnx was not found above the test assembly.");
    }
}

/// <summary>Applies the contract's stable-value rule to served selector pages, so two runs give identical files.</summary>
/// <remarks>
///   The rule of <see cref="ContractNormalizer"/>, with the selector item id on top: every Guid becomes
///   <c>00000000-0000-7000-8000-0000000000NN</c> by first appearance, one numbering across the folder in file order; a
///   cursor keeps <c>base64("{subscription:N}:{tx}:{row}")</c> with the transaction id as its ordinal; an <c>itemId</c> is
///   recomputed over the renumbered post by the writer's rule, <c>sha256("{postId:N}:{revision}:{type}")</c> for ingest, and
///   <c>sha256("{postId:N}:{revision}:{type}:s{selectorVersion}")</c> when the event's <c>match</c> names the selector
///   version that a change wrote it under. A served id that follows neither form fails the run.
///   The folder keeps its own numbering and normalizer, since the channel pages' normalizer takes only the first form.
/// </remarks>
/// <param name="wire">The host's JSON options, which encode a replacement exactly as the host would.</param>
internal sealed partial class SelectorContractNormalizer(JsonSerializerOptions wire)
{
    private const string StablePrefix = "00000000-0000-7000-8000-";

    private readonly Dictionary<Guid, Guid> guids = [];
    private readonly Dictionary<ulong, ulong> transactions = [];

    /// <summary>Returns the stable bytes of one served answer.</summary>
    /// <param name="served">The answer's bytes as the host served them.</param>
    public byte[] Normalize(byte[] served)
    {
        var (cursors, items) = Facts(served);

        // Pass 1, in document order: number Guids and cursors by first appearance and remember each string token.
        var tokens = new List<(int Start, int Length, string Value)>();
        var reader = new Utf8JsonReader(served);
        while (reader.Read())
        {
            if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName))
                continue;
            var start = checked((int)reader.TokenStartIndex);
            if (served[start] != (byte)'"' || reader.HasValueSequence)
                throw new InvalidOperationException($"Unexpected string token at byte {start}.");
            var value = reader.GetString()!;
            tokens.Add((start, reader.ValueSpan.Length + 2, value));
            if (IsGuid(value, out var guid))
                Map(guid);
            else if (cursors.Contains(value))
                Cursor(value);
        }

        // Pass 2: compute replacements now that every id has its number.
        var output = new List<byte>(served.Length);
        var position = 0;
        foreach (var (start, length, value) in tokens)
        {
            string? replacement = IsGuid(value, out var guid) ? Map(guid).ToString("D")
                : cursors.Contains(value) ? Cursor(value)
                : items.TryGetValue(value, out var item) ? ItemId(Map(item.Post), item.Revision, item.Type, item.SelectorVersion)
                : null;
            if (replacement is null || replacement == value)
                continue;
            output.AddRange(served.AsSpan(position, start - position));
            output.AddRange(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(replacement, wire)));
            position = start + length;
        }
        output.AddRange(served.AsSpan(position));
        var stable = output.ToArray();

        var text = Encoding.UTF8.GetString(stable);
        foreach (Match match in LooseGuid().Matches(text))
            if (!match.Value.StartsWith(StablePrefix, StringComparison.Ordinal))
                throw new InvalidOperationException($"An unstable id survived normalization: {match.Value}.");
        return stable;
    }

    /// <summary>Computes an event's item id the way the module's delivery-event writer does: with the selector version's
    /// <c>:s{n}</c> suffix when <paramref name="selectorVersion"/> is set.</summary>
    public static string ItemId(Guid post, int revision, string type, int? selectorVersion = null) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{post:N}:{revision}:{type}" + (selectorVersion is { } version ? $":s{version}" : string.Empty)))).ToLowerInvariant();

    private static (HashSet<string> Cursors, Dictionary<string, (Guid Post, int Revision, string Type, int? SelectorVersion)> Items) Facts(byte[] served)
    {
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var items = new Dictionary<string, (Guid Post, int Revision, string Type, int? SelectorVersion)>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(served);
        if (!document.RootElement.TryGetProperty("data", out var page) || page.ValueKind != JsonValueKind.Object
            || !page.TryGetProperty("next", out var next) || next.ValueKind != JsonValueKind.String)
            return (cursors, items);
        cursors.Add(next.GetString()!);
        foreach (var item in page.GetProperty("data").EnumerateArray())
        {
            var itemId = item.GetProperty("itemId").GetString()!;
            var post = item.GetProperty("postId").GetGuid();
            var revision = item.GetProperty("revision").GetInt32();
            var type = item.GetProperty("type").GetString()!;
            int? version = item.TryGetProperty("match", out var match) && match.TryGetProperty("selectorVersion", out var number) ? number.GetInt32() : null;
            if (ItemId(post, revision, type) == itemId)
                version = null;
            else if (version is null || ItemId(post, revision, type, version) != itemId)
                throw new InvalidOperationException($"Item {itemId} follows neither sha256(postId:N:revision:type) nor its selector-version form.");
            items[itemId] = (post, revision, type, version);
        }
        return (cursors, items);
    }

    private Guid Map(Guid served)
    {
        if (!guids.TryGetValue(served, out var stable))
        {
            stable = Guid.ParseExact(StablePrefix + (guids.Count + 1).ToString("D12", CultureInfo.InvariantCulture), "D");
            guids.Add(served, stable);
        }
        return stable;
    }

    private string Cursor(string served)
    {
        var parts = Encoding.UTF8.GetString(Convert.FromBase64String(served)).Split(':');
        if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out var subscription)
            || !ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var transaction))
            throw new InvalidOperationException("The cursor no longer reads as {subscription:N}:{tx}:{row}.");
        if (!transactions.TryGetValue(transaction, out var ordinal))
        {
            ordinal = (ulong)transactions.Count + 1;
            transactions.Add(transaction, ordinal);
        }
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{Map(subscription):N}:{ordinal}:{parts[2]}")));
    }

    private static bool IsGuid(string value, out Guid guid)
    {
        guid = Guid.Empty;
        return value.Length == 36 && Guid.TryParseExact(value, "D", out guid);
    }

    [GeneratedRegex("(?<![0-9a-fA-F-])[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(?![0-9a-fA-F-])")]
    private static partial Regex LooseGuid();
}
