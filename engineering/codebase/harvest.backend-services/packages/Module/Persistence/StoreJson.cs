using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;

/// <summary>Writes and reads the store's jsonb columns in the SDK's wire shape, so a column reads as its API model does.</summary>
internal static class StoreJson
{
    /// <summary>Serializes a value for a jsonb column.</summary>
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, JsonOptionsConstants.Default);

    /// <summary>Deserializes a jsonb column, or returns <paramref name="fallback"/> for an empty document.</summary>
    public static T Read<T>(string json, T fallback) where T : class =>
        string.IsNullOrWhiteSpace(json) ? fallback : JsonSerializer.Deserialize<T>(json, JsonOptionsConstants.Default) ?? fallback;
}
