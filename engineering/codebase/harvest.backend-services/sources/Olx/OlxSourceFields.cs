using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Olx;

/// <summary>Describes the visible OLX fields and selected provider JSON retained by the reader.</summary>
internal static class OlxSourceFields
{
    internal const string Schema = "olx.ad.v1";

    internal static JsonElement JsonSchema { get; } = JsonSerializer.Deserialize<JsonElement>($$"""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["schema", "parserVersion", "fields"],
          "additionalProperties": false,
          "$defs": {
            "nullableText": { "type": ["string", "null"] },
            "nativeObject": { "type": ["object", "null"] },
            "nativeArray": { "type": ["array", "null"] }
          },
          "properties": {
            "schema": { "const": "{{Schema}}" },
            "parserVersion": { "type": "string" },
            "fields": {
              "type": "object",
              "required": ["externalId", "title", "attributes"],
              "additionalProperties": false,
              "properties": {
                "externalId": { "type": "string" },
                "title": { "type": "string" },
                "descriptionHtml": { "$ref": "#/$defs/nullableText" },
                "priceText": { "$ref": "#/$defs/nullableText" },
                "postedAtText": { "$ref": "#/$defs/nullableText" },
                "sellerName": { "$ref": "#/$defs/nullableText" },
                "attributes": { "type": "object", "additionalProperties": { "type": "string" } },
                "originalProduct": { "$ref": "#/$defs/nativeObject" },
                "categoryPath": { "$ref": "#/$defs/nullableText", "description": "A published real-estate category path." },
                "id": { "type": ["integer", "string", "null"] },
                "category": { "$ref": "#/$defs/nativeObject" },
                "map": { "$ref": "#/$defs/nativeObject" },
                "isBusiness": { "type": ["boolean", "null"] },
                "url": { "$ref": "#/$defs/nullableText" },
                "createdTime": { "$ref": "#/$defs/nullableText" },
                "lastRefreshTime": { "$ref": "#/$defs/nullableText" },
                "validToTime": { "$ref": "#/$defs/nullableText" },
                "isActive": { "type": ["boolean", "null"] },
                "status": { "$ref": "#/$defs/nullableText" },
                "params": { "$ref": "#/$defs/nativeArray" },
                "itemCondition": { "$ref": "#/$defs/nullableText" },
                "price": { "$ref": "#/$defs/nativeObject" },
                "salary": { "description": "Provider-native salary data when included for the category." },
                "isJob": { "type": ["boolean", "null"] },
                "photos": { "$ref": "#/$defs/nativeArray" },
                "location": { "$ref": "#/$defs/nativeObject" }
              }
            }
          }
        }
        """);
}
