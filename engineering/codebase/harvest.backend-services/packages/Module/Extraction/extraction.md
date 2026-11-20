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
`norm@1` and `rules@5`.

- `Extras.schemaOrg` retains the original Event node; `Extras.event` contains the deterministic `wow2.event.v1` payload
- `EventSchemaMapper.Map(envelope)` exposes that event mapping for a subscription's schema selection
- the mapping fills `organizer` only from a declared organizer (`FieldSources.organizer`), never from a byline or
  publisher; it carries `postedAt` and a `mapping` stamp `{ schema, norm, rules, parser }`
- `classification` `{ classifier, verdict: relevant | irrelevant | uncertain, confidence, reason, kind }` and the
  top-level `kind` come from `PostEnvelope.Classification`, which the `post-tags` pass writes (S16); both stay null
  while it does not
- `starts` and `ends` carry `certainty`: `stated`, or `estimated` for a date the extraction pass estimated (S17)
- `titles` lists the English title the post-tags pass wrote (`Extras.postTags.titleEn`) as
  `{ text, language: "en", machine: true }` right after `title`; the property is absent when the post has none
- dates retain stated year/month/day/time precision and offset. Invalid or relative dates remain unresolved
- an application deadline a named reader states in its namespace (`schemaOrg.craftfair.applicationDeadline`,
  `schemaOrg.uzexpocentre.applicationDeadline`, `schemaOrg.afisha.applicationDeadlineEvidence.date`) becomes a
  `DateRole.Deadline` date with its precision, raw text and `CallStatus` (Craftfair's status badge: `closed` or `open`,
  else `unknown`) since `rules@5`; the namespace stays untouched (`ApplicationDeadlines`)
- the mapping's `deadlines` lists `{ kind: application, value, precision, raw, status }` per stated deadline, `[]` when
  none; an envelope from before `rules@5` reads them from its stored namespace
- prices retain stated currency and amount, range or free status; absence never becomes zero
- local Uzbek numbers gain `+998` only for an Uzbek source and a recognized local prefix; international numbers
  require a country code. Contacts remain normalized plain text
- the contact rule (`e164-and-email@3`; `rules@5` is unchanged, the golden pages carry it) reads the title, the text, the
  reader's `contacts` metadata (`tel:` and `mailto:` links), the resolved author name (schema.org seller, metadata
  `author` or publisher) and every string value of the reader's listing attributes (`sourceFields.fields.attributes`;
  objects and arrays are walked, other values ignored); the author name and each attribute value go in on their own
  line, so two of them never join into one number. `Author`, `Title` and `Text` keep what they hold. The listing prompt never renders the author name or the
  contacts, and `PhoneScrub` still scrubs the title and text it renders
- language rules use explicit page/feed locale first, then script and small Uzbek/English lexicons; uncertain text stays `und`
- requesting an AI tier reports `ai_skipped:capability_unavailable`; no model service is called

## Boundaries

Text-date rules cover ISO dates and `DD.MM.YYYY`, without inventing a year. Relative dates, natural-language month
phrases, ambiguous local phone numbers, entity resolution and broader language classification remain consumer or
future extraction work. The event mapper preserves missing fields; it does not imply validation against a
consumer-provided JSON Schema, whose registration is beyond this slice.
