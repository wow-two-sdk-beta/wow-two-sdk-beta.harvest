using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Harvest.Tests.E2E.Contract;

/// <summary>Applies the contract's stable-value rule to served bytes, so two runs give identical files.</summary>
/// <remarks>
///   - every Guid becomes <c>00000000-0000-7000-8000-0000000000NN</c>, numbered in order of first appearance; one map
///     spans the folder's files in their order, so an id equal in two files stays equal
///   - an <c>itemId</c> is recomputed by the writer's rule, <c>sha256("{postId:N}:{revision}:{type}")</c>, over the
///     renumbered post; the served one must match the rule over the served post
///   - a cursor keeps its shape, <c>base64("{subscription:N}:{tx}:{row}")</c>: the subscription is renumbered, the
///     PostgreSQL transaction id becomes its ordinal, the row id stays as served; equal cursors stay equal
///   - everything else stays byte for byte as served; replacement strings are encoded with the host's own JSON options
/// </remarks>
/// <param name="wire">The host's JSON options, which encode a replacement exactly as the host would.</param>
internal sealed partial class ContractNormalizer(JsonSerializerOptions wire)
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
                : items.TryGetValue(value, out var item) ? ItemId(Map(item.Post), item.Revision, item.Type)
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

    /// <summary>Computes an event's item id the way the module's delivery-event writer does.</summary>
    public static string ItemId(Guid post, int revision, string type) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{post:N}:{revision}:{type}"))).ToLowerInvariant();

    private static (HashSet<string> Cursors, Dictionary<string, (Guid Post, int Revision, string Type)> Items) Facts(byte[] served)
    {
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var items = new Dictionary<string, (Guid Post, int Revision, string Type)>(StringComparer.Ordinal);
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
            if (ItemId(post, revision, type) != itemId)
                throw new InvalidOperationException($"Item {itemId} no longer follows sha256(postId:N:revision:type).");
            items[itemId] = (post, revision, type);
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
