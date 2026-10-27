# Extraction/

Deterministic and rule tiers implement `IPostExtractor` without model or provider integration.

| Implements (Abstractions) | Produces |
|---|---|
| `Extraction.IPostExtractor` | `Extraction.PostEnvelope` (`wow2.post.v1`) for every capture, plus the `wow2.event.v1` mapping from `extras.schemaOrg` |

- Tiers run D → R → E → S → L; E, S and L stay off in v0.1 (`ExtractionRequest.Ai`, `AiAllowed`, budget).
- Normalization `norm@1`; dates keep their precision; prices as published, never zero for unknown.
- Contact numbers are stored as plain text, normalized (phone to E.164), and matched directly for dedup.
- Writes: `harvest_link_readings.fields` and `tier_map` for the Posts lane to resolve.

## Composition

`AddHarvestExtraction()` registers `RulesPostExtractor`. Structured Event fields win; text rules fill missing dates,
prices and contacts. Every filled field records its parser or rule in `FieldSources`, and each envelope records
`norm@1` and `rules@1`.

- `Extras.schemaOrg` retains the original Event node; `Extras.event` contains the deterministic `wow2.event.v1` payload
- `EventSchemaMapper.Map(envelope)` exposes that event mapping for a subscription's schema selection
- dates retain stated year/month/day/time precision and offset. Invalid or relative dates remain unresolved
- prices retain stated currency and amount, range or free status; absence never becomes zero
- local Uzbek numbers gain `+998` only for an Uzbek source and a recognized local prefix; international numbers
  require a country code. Contacts remain normalized plain text
- language rules use explicit page/feed locale first, then script and small Uzbek/English lexicons; uncertain text stays `und`
- requesting an AI tier reports `ai_skipped:capability_unavailable`; no model service is called

## Boundaries

Text-date rules cover ISO dates and `DD.MM.YYYY`, without inventing a year. Relative dates, natural-language month
phrases, ambiguous local phone numbers, entity resolution and broader language classification remain consumer or
future extraction work. The event mapper preserves missing fields; it does not imply validation against a
consumer-provided JSON Schema, whose registration is beyond this slice.
