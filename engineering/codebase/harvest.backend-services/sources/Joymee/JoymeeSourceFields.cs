using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Joymee;

/// <summary>Describes the public Joymee listing facts retained by the reader.</summary>
internal static class JoymeeSourceFields
{
    internal const string Schema = "joymee.ad.v1";

    internal static JsonElement JsonSchema { get; } = JsonSerializer.Deserialize<JsonElement>($$"""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["schema", "parserVersion", "fields"],
          "additionalProperties": false,
          "$defs": {
            "nullableText": { "type": ["string", "null"] },
            "providerScalar": { "type": ["string", "number", "null"] }
          },
          "properties": {
            "schema": { "const": "{{Schema}}" },
            "parserVersion": { "type": "string" },
            "fields": {
              "type": "object",
              "required": ["title", "imageUrls", "attributes"],
              "additionalProperties": false,
              "properties": {
                "title": { "type": "string" },
                "description": { "$ref": "#/$defs/nullableText" },
                "priceText": { "$ref": "#/$defs/nullableText" },
                "postedAtText": { "$ref": "#/$defs/nullableText" },
                "sellerName": { "type": "string" },
                "city": { "$ref": "#/$defs/nullableText" },
                "district": { "$ref": "#/$defs/nullableText" },
                "imageUrls": { "type": "array", "items": { "type": "string", "format": "uri" } },
                "attributes": { "type": "object", "additionalProperties": { "type": "string" } },
                "externalId": { "type": "string" },
                "addressLine": { "$ref": "#/$defs/nullableText" },
                "advertiserType": { "$ref": "#/$defs/providerScalar" },
                "publisherVerified": { "type": ["boolean", "null"] },
                "previousPrice": { "$ref": "#/$defs/providerScalar" },
                "category": { "$ref": "#/$defs/providerScalar" },
                "dealType": { "$ref": "#/$defs/providerScalar" },
                "propertyType": { "$ref": "#/$defs/providerScalar" },
                "detail": {
                  "type": ["object", "null"],
                  "additionalProperties": false,
                  "properties": {
                    "apartment_type": { "$ref": "#/$defs/providerScalar" },
                    "area_m2": { "$ref": "#/$defs/providerScalar" },
                    "floor_number": { "$ref": "#/$defs/providerScalar" },
                    "floors_count": { "$ref": "#/$defs/providerScalar" },
                    "repair": { "$ref": "#/$defs/providerScalar" },
                    "room_quantity": { "$ref": "#/$defs/providerScalar" }
                  }
                },
                "pricing": {
                  "type": ["object", "null"],
                  "additionalProperties": false,
                  "properties": {
                    "currency": { "$ref": "#/$defs/providerScalar" },
                    "currency_display": { "$ref": "#/$defs/nullableText" },
                    "price": { "$ref": "#/$defs/providerScalar" },
                    "price_from": { "$ref": "#/$defs/providerScalar" },
                    "price_to": { "$ref": "#/$defs/providerScalar" }
                  }
                },
                "location": {
                  "type": ["object", "null"],
                  "additionalProperties": false,
                  "properties": {
                    "latitude": { "$ref": "#/$defs/providerScalar" },
                    "longitude": { "$ref": "#/$defs/providerScalar" }
                  }
                },
                "media": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "required": ["file"],
                    "additionalProperties": false,
                    "properties": {
                      "file": {
                        "type": "object",
                        "required": ["url"],
                        "additionalProperties": false,
                        "properties": {
                          "id": { "$ref": "#/$defs/providerScalar" },
                          "url": { "type": "string", "format": "uri" }
                        }
                      },
                      "order": { "$ref": "#/$defs/providerScalar" },
                      "is_cover": { "type": ["boolean", "null"] }
                    }
                  }
                }
              }
            }
          }
        }
        """);
}
