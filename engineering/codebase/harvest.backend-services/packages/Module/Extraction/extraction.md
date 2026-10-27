# Extraction/

The extraction cascade — iteration 5, tiers D and R.

| Implements (Abstractions) | Produces |
|---|---|
| `Extraction.IPostExtractor` | `Extraction.PostEnvelope` (`wow2.post.v1`) for every capture, plus the `wow2.event.v1` mapping from `extras.schemaOrg` |

- Tiers run D → R → E → S → L; E, S and L stay off in v0.1 (`ExtractionRequest.Ai`, `AiAllowed`, budget).
- Normalization `norm@1`; dates keep their precision; prices as published, never zero for unknown.
- Contact numbers are stored as plain text, normalized (phone to E.164), and matched directly for dedup.
- Writes: `harvest_link_readings.fields` and `tier_map` for the Posts lane to resolve.
