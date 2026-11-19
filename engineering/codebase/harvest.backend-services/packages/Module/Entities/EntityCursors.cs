using System.Globalization;
using System.Text;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Encodes the opaque cursors of the entity reads: the change feed's <c>(transaction, row id)</c> position, bound
/// to its container as the subscription pull binds its cursor to its subscription, and an entity's posts' position in
/// their newest-first order, bound to the entity.</summary>
internal static class EntityCursors
{
    private const int MaxLength = 512;

    /// <summary>Encodes a change feed position.</summary>
    internal static string Feed(Guid containerId, ulong transactionId, long rowId) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{containerId:N}:{transactionId.ToString(CultureInfo.InvariantCulture)}:{rowId.ToString(CultureInfo.InvariantCulture)}"));

    /// <summary>Decodes a change feed position; an empty cursor is the start. False when malformed or of another container.</summary>
    internal static bool TryFeed(Guid containerId, string? cursor, out ulong transactionId, out long rowId)
    {
        transactionId = 0;
        rowId = 0;
        if (string.IsNullOrEmpty(cursor))
            return true;
        if (Decode(cursor) is not { } text)
            return false;
        var parts = text.Split(':');
        return parts.Length == 3 && Guid.TryParseExact(parts[0], "N", out var owner) && owner == containerId
            && ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out transactionId)
            && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out rowId);
    }

    /// <summary>Encodes the position after a post of an entity's newest-first list.</summary>
    internal static string Posts(Guid entityId, Guid postId, string sortKey) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{entityId:N}:{postId:N}:{sortKey}"));

    /// <summary>Decodes an entity's posts position; an empty cursor is the start. False when malformed or of another entity.</summary>
    internal static bool TryPosts(Guid entityId, string? cursor, out string? sortKey, out Guid postId)
    {
        sortKey = null;
        postId = Guid.Empty;
        if (string.IsNullOrEmpty(cursor))
            return true;
        if (Decode(cursor) is not { } text)
            return false;
        var parts = text.Split(':', 3);
        if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out var owner) || owner != entityId
            || !Guid.TryParseExact(parts[1], "N", out postId) || parts[2].Length == 0)
            return false;
        sortKey = parts[2];
        return true;
    }

    private static string? Decode(string cursor)
    {
        if (cursor.Length > MaxLength)
            return null;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
