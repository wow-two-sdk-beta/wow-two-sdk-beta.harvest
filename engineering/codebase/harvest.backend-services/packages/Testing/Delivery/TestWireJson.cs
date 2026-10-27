using System.Text.Json;
using System.Text.Json.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;

/// <summary>Serializes the fake's snapshots with the public wire conventions without depending on core implementation.</summary>
internal static class TestWireJson
{
    internal static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
