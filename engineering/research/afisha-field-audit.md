# Afisha fields, availability and analysis inputs

Reviewed: 2026-10-27. Scope: Afisha's public HTML and the local Harvest preview; no organizer was contacted.

## Findings and correction

The [MiningMetals announcement](https://www.afisha.uz/ru/exhibitions/2026/09/02/miningmetals-2026) exposed two different gaps:

- Dates, venue, coordinates and the cover image already reached the mapped post but the console hid them.
- The article body, phone and original tags did not reach the post correctly.

The stored Material object's `content` was `$40`. This is a React Flight reference to an embedded HTML text record,
not a price or article text. The original reader treated that reference literally and retained only the introduction.
The current page can use a different ID, so the correction resolves records by ID and their UTF-8 byte length,
including records split across script chunks. Unresolved references fall back to visible article content and emit a
warning. Scripts are never executed. React's [server implementation](https://github.com/facebook/react/blob/main/packages/react-server/src/ReactFlightServer.js)
defines large-string reference serialization and length-delimited text records.

The source phone also contains Unicode minus signs. Extraction now accepts those separators and preserves the
published text alongside `+998712051414`. The number's ownership, purpose and current reachability have not been
independently verified. Formatting or numbering-plan validation cannot establish those facts; see Google's
[libphonenumber FAQ](https://github.com/google/libphonenumber/blob/master/FAQ.md#what-does-it-mean-for-a-phone-number-to-be-valid).

## Field audit

The following are source observations, not an inference that this industrial exhibition fits Ocharo.

| Field group | MiningMetals evidence | Representation and constraint |
|---|---|---|
| Identity | Material `/api/materials/75223`, locale `ru`, category `exhibitions`, canonical article URL | Source ID, external key, canonical and fetched URLs, locale, source schema/parser version |
| Editorial type | Announcement; native Material type `EVENT` | Keep native type separately from mapped `event`, `article` or `call` |
| Publication | 2 September 2026; embedded timestamp `2026-09-02T13:13:47+05:00` | Publication timestamp and its original offset; separate from event dates and observation times |
| Content | Introduction, full paragraphs, programme details and sector list | Native introduction remains separate; mapped body preserves paragraph/list line breaks and falls back to the introduction only when the body is absent |
| Event dates | 27–29 October 2026 | Start/end with day precision; preserve all native schedules and exact-time flags; do not invent opening hours |
| Place | Uzexpocentre, Tashkent, Amir Temur 107; source coordinates 41.3389, 69.2799 | Venue entity, address components, coordinates and evidence; publisher footer address is separate |
| Organizers | Iteca Exhibitions and ICA Eurasia Group named in the body | Preserve source names; multiple organizer entities need evidence-backed mapping, not attribution to the article author |
| Programme | UIMF 2026; sector list and participation figures in the body | Source text/sections now; typed programme and audience fields when mapped with evidence |
| Admission | Advance registration is required | Visitor requirement evidence; this does not prove an application is currently open |
| Phone | Public event phone normalizes to `+998712051414` | Raw value plus normalized value; owner and purpose remain unresolved unless explicitly stated |
| Source tags | `iteca`, `выставка`, `горное дело`, `узэкспоцентр` | `sourceTags`, kept separate from shared taxonomy tags or AI classifications |
| Media | Cover photo; native media metadata supports dimensions, captions, author and variants | Source media URL and native metadata; no invented image description or OCR facts |
| Engagement | Capture-time views counter 325; other native counters present | Snapshot observations, not content facts; exclude counters from semantic content hashing |
| Fees and purchase links | No reliable numeric ticket price in the audited body; a generic ticket affordance/SEO is insufficient | Unknown fee/currency unless explicitly stated; a label is not a verified registration or payment route |

The source's SEO description also contains generic concert wording and a nonnumeric price. Those must not outrank
the article and event fields. Afisha's footer email and publisher address must not become event contact details.

For other Afisha posts, preserve optional author, photographer/credit, galleries, embedded video, ticket/registration
URLs, complete schedules, venue details, explicit prices/currency, age restrictions, accessibility, cancellation or
rescheduling notices, application deadlines and participation requirements whenever the source actually states them.
Their absence is unknown, not false, free or unrestricted. [Schema.org Event](https://schema.org/Event) supplies useful
common names for dates, location, organizers, offers, media and event status; it does not establish a source value.

## Initial fields, mapped fields and AI results

Keep four distinct layers. A new source can declare different fields without adding a table column for each one.

| Layer | Current storage | Contract and next step |
|---|---|---|
| Raw capture | Immutable HTML bytes in `harvest_captures.body_inline`, subject to retention policy | Preserve request/final URL, HTTP status, captured time and hashes; this is the recovery source for unknown fields |
| Initial source fields | Afisha emits `extras.source` with `schema: afisha.material.v1`, `parserVersion: afisha-html@4`, original Material `fields`, resolved `contentHtml`, and original JSON-LD `originalEvent`; `PostLinkModel.SourceFields` exposes each link's own snapshot | Native keys and values remain intact, including nulls and unmapped fields; this snapshot is distinct from the raw transport and the merged post's projection |
| Mapped/shared fields | `wow2.post.v1`, deterministic `wow2.event.v1`, field sources, `norm@1`/`rules@2` stamps | Dates, contacts, media, locations and original source tags are visible; taxonomy tags remain a separate collection |
| AI analysis | No model result is generated by this correction | A future separate result references its exact reading/input hash and evidence; it must not overwrite native values |

The current `harvest_link_readings` row is the latest reading per link, not an immutable history of every mapping.
Delivery revisions are immutable snapshots, and raw capture bytes are retained according to policy. These are useful
existing capabilities but do not replace a versioned source-reading history.

The next storage extension should add immutable source readings and mapping results, with the current post remaining
a resolved projection. A source declaration should carry a versioned JSON Schema, schema hash, parser version,
declared field types/labels and unknown-field policy. A mapping profile should identify its input schema/version,
target schema/version and mapping version. Native fields can stay in JSONB, while common query fields such as event
dates, phones and location receive appropriate indexes. Avoid an entity-attribute-value table for the whole post.
[JSON Schema object validation](https://json-schema.org/understanding-json-schema/reference/object) supports declared
properties and explicit handling of additional properties; schema registration and enforcement remain future work.

An evidence record should identify the source reading and capture, source URL, JSON pointer or DOM/text span, raw
value, mapping rule, confidence when meaningful, and parser/model version. Distinguish observed values, inferred
values, ambiguity, parse failure and values not published. A human override should add actor, reason and time as its
own layer. This follows the derivation and attribution concepts in [W3C PROV-O](https://www.w3.org/TR/prov-o/) without
requiring RDF storage.

Example source-specific envelope already emitted by the reader:

```json
{
  "schema": "afisha.material.v1",
  "parserVersion": "afisha-html@4",
  "fields": {
    "@id": "/api/materials/75223",
    "type": "EVENT",
    "content": "$40",
    "tags": ["iteca", "выставка", "горное дело", "узэкспоцентр"]
  },
  "contentHtml": "<resolved source body, stored as data>",
  "originalEvent": {}
}
```

This example abbreviates the native object; extraction retains all of its supplied fields. A Flight reference can
change between otherwise identical responses. Resolved semantic content drives revisions; reference IDs, view
counters and transport times do not. An updated parser version can deliberately create a corrected reading.

## Contact fields for AI analysis

The current shared contact record contains `kind`, normalized `value` and `raw`. Extend it through a versioned
contact/evidence model rather than adding a single unqualified phone string to an event.

| Field | Why it matters |
|---|---|
| Kind and raw value | Distinguish phone, email, website, Telegram, Instagram and other published contact routes |
| Normalized value | E.164 candidate, canonical email/URL or explicit platform handle; keep the original text |
| Extension and country basis | Explain how a local number was interpreted; do not assume every foreign organizer has a local number |
| Owner entity | Organizer, venue, ticket seller, exhibitor coordinator, author or publisher; unknown is allowed |
| Purpose/participation route | Visitor registration, exhibitor application, artisan/vendor sales, runway casting, press or general enquiries |
| Evidence and observation time | Source reading, exact text/anchor, page URL and captured time |
| Validation state | Parsed, possible, numbering-plan validated, ambiguous, invalid; never equate formatting with ownership or reachability |
| Languages/hours | Retain only explicit published details |

[Schema.org ContactPoint](https://schema.org/ContactPoint) covers telephone, email, contact purpose, languages and
availability. These are modeling references, not permission to contact anyone. The current work makes no calls,
sends no messages and performs no contact verification.

## Inputs and outputs for Ocharo analysis

AI should receive the full source body and original labels; publication/event/deadline distinctions; all schedules
and timezone/precision evidence; location; organizers; media references and credits; public contacts; admission,
fees and registration/application links; current source availability; and explicit missing-field/parser warnings.
Publisher navigation, unrelated recommendations and footer contacts should stay outside the event context.

Useful derived outputs are event type and fashion/craft relevance; geography; audience; distinct visitor, exhibitor,
vendor, runway and artisan routes; eligibility; deadline candidates; proposed next action; duplicate candidates;
and missing information. Each nontrivial conclusion needs evidence and an inference/review marker. A live article
does not prove that visitor registration or exhibitor applications are open.

Keep the analysis append-only with input reading IDs/hashes, model identifier, prompt version, output schema version,
run time, confidence/evidence, review status and cost. OCR and translations should similarly identify their input
image/text and version. Raw source facts, mapped facts, model conclusions and manual corrections stay inspectable.
The existing source and pipeline AI gates remain off; this research does not enable a model provider.

## What “last checked” means

Before this change, the database recorded availability times but the post API and console omitted them. The MiningMetals
record's audited observation was 2026-10-27 22:11:44 Asia/Tashkent, from its initial capture run. That was positive
capture evidence, not proof that a later dedicated deletion check had run. All local preview check schedules were off.

The API now exposes each link's content-capture time, last recorded availability observation, last confirmed-present
time, outcome, HTTP status, run ID/kind, observation count and next eligibility time. The console distinguishes a
capture observation and displays whether automatic check schedules are enabled. A cadence eligibility timestamp is
not a promised next execution. Re-extracting saved bytes preserves the original observation time.

Existence is per source link. A merged post can have one deleted link and another available link; neither state
determines whether the event was cancelled. Current 404 policy requires another independent strong negative; a 410
can establish disappearance immediately. Authentication, CAPTCHA, rate limiting and network failures must remain
inconclusive.

Two engine gaps remain explicit: the current check log stores conclusive link observations rather than every failed
attempt, and a generic HTTP 200 check does not yet verify source-specific material identity/soft deletion. A future
per-link “Check now” operation should bypass cadence eligibility while retaining robots, authorization and host
backoff policies; the present pipeline-wide check action is due-only. Do not present that action as a forced per-post check.

## Acceptance evidence and remaining work

Implemented here: Flight resolution and fallback warnings; full text; original tags; Unicode phone extraction;
article-only contact anchors; native source snapshots; mapped contacts/source tags in event projection; counters
outside the semantic hash; post availability read fields; and the richer post detail panel.

Regression coverage includes split UTF-8 Flight records, short JSON string records, missing/truncated references,
counter-only changes, separation of source tags from taxonomy, Unicode phone normalization, inert captured HTML in
the console, missing-check timestamps, PostgreSQL query evidence and replay without artificial freshness.

Remaining: registered/enforced source schemas; immutable source/mapping reading history; richer contact ownership
and purpose; explicit organizer/participation mapping; rich block/media associations; metrics history; per-attempt
check logging; source-aware soft-deletion checks; forced per-link checks; and the separate AI-analysis result store.
