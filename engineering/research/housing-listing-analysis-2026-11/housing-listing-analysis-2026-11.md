# Housing listing data and shared SCFD model

Reviewed 2026-11-15 in the local Haven SCFD stack. The proposed SCFD projection is `wow2.listing.v1`. Every housing post can carry this projection even when the source supplies only a title and a description.

## Sample and coverage

**1,005 observations, 1,003 verified distinct ads.**

| Evidence | Rows | What it covers |
|---|---:|---|
| Archived OLX | 931 | 18 structured keys and the historical value distributions |
| Earlier local SCFD OLX | 10 | Full per-link native fields and exact structured prices |
| New local SCFD Joymee | 64 | Fresh long-term Tashkent apartment ads, including 64 retained JSON detail bodies |

One canonical OLX ad ID appears in the archive and the earlier SCFD captures. A legacy archive identifier could not be matched to the current OLX ID format and is counted only as a profiled row. Public details and fixtures for BirBir and Domtut establish fields outside these measured sample totals.

The unit of measurement is a provider ad. A relist, translated URL or repeat capture can refer to the same ad. The count uses provider plus external AD ID. The archive has 931 distinct raw IDs, including one legacy non-token ID kept in the profiling rows. One-time samples ran in a private research container with zero schedules, subscriptions or AI calls. The aggregate inventory contains counts and types. [Aggregate inventory](sample-profile.json)

Joymee finished at the 64-ad cap after 72 requests with zero failed steps. Its SCFD status is `partial / itemLimit`. OLX's worker could not resolve the host on the `robots.txt` request. The intervening retry made zero requests during host backoff and the retry after backoff encountered the same DNS error. The earlier SCFD OLX records supply the current adapter comparison. The inventory records aggregate coverage.

To reproduce the measures, profile the archived `batch*.json` exports and read the two containers through `GET /api/posts` on the local SCFD host. Deduplicate `links[].externalKey` within a provider before joining the archive. The link's `sourceFields.fields` provides the materialized native data. Aggregate JSON paths, types and allowed code counts from the retained Joymee capture bodies for the sampled run. Count nonempty fields and nearest-rank p95 for each metric. Parse whitespace and comma decimal conventions for magnitude statistics while keeping the raw units unresolved where necessary.

The archived OLX rows come from a 2026-03-02 classifier batch export. Its selection favored unprocessed rows. The live runs target newest long-term apartment rentals and reflect the providers and cities in their saved scopes. Catalog checks on houses, daily rent and new developments extend the taxonomy. Treat measured coverage as sample coverage. [Historical export query](../../../../../maxs-projects/ventures/saas/10x-ven-haven/engineering/research/classification/prompt-improvement/scripts/extract-batches.sql), [Historical OLX report](../../../../../maxs-projects/ventures/saas/10x-ven-haven/engineering/research/classification/olx-listing-data-report.md)

### OLX structured attributes in 931 archived rows

| Native label | Present | Canonical target |
|---|---:|---|
| Количество комнат | 931 | Unit room count |
| Общая площадь | 931 | Total area and declared basis |
| Этаж | 931 | Unit level |
| Этажность дома | 931 | Building floor count |
| Меблирована | 931 | Furnishing state |
| Комиссионные | 931 | Commission applicability |
| Ремонт | 789 | Condition, finish and renovation level |
| Санузел | 692 | Bathroom arrangement or count bound |
| Тип строения | 686 | Construction material or structural system |
| Рядом есть | 658 | Nearby facilities |
| В квартире есть | 641 | In-unit features |
| Планировка | 604 | Layout and room connectivity |
| Тип жилья | 511 | New-build or resale segment |
| Высота потолков | 373 | Ceiling height |
| Жилая площадь | 320 | Living area |
| Площадь кухни | 194 | Kitchen area |
| Год постройки | 151 | Construction year or range |
| Год постройки/сдачи | 143 | Year or range with the built-or-handover role |

The seller badge appears on all 931 rows (765 business, 166 private individual). Other observations include 729 furnished, 351 commission yes and 401 negotiable. Brick (452), panel (113), monolith (107), block (11) and wood (3) are initial construction vocabulary. Layout labels include separate (477), studio (43), adjacent (36), mixed adjacent and separate (33), multilevel (11), compact family (3) and penthouse (1). Store room connectivity and unit style independently. The 145 ads listing two or more bathrooms supply a lower bound. The 346 separate and 201 combined values describe arrangement.

Archive rooms have median 2 and p95 4. Parsed total area has median 64 and p95 150. Floor has median 4 and p95 12. Building floors have median 9 and p95 17. These statistics use nearest-rank p95 and include decoded digit separators. Observed area 88,888 and building floors 8,888 illustrate why permissive numeric ingestion needs quality flags. Three floors exceed their building floor count, two living areas exceed total area, and 124 records with all three areas have living plus kitchen greater than total. Area labels can use different coverage conventions.

### Source shapes and missing mappings

| Surface | Verified structure | Mapping work |
|---|---|---|
| OLX | Existing `olx.ad.v1` keeps localized attributes and selected hydration fields, including category, map, business indicator, native timestamps, status, location, product, photos and price. Prior captures have exact USD in structured price as well as the display marker. Houses expose a separate lot area and utility options. Daily searches expose capacity as a filter. | Map native code and localized labels by category and locale. Keep house lot units, build-year precision, promotion, separate posted/refresh/expiration timestamps, and denormalized price and media evidence. Track capacity as a listing field when an ad supplies it. |
| Joymee | 64 captured JSON detail bodies. Current `joymee.ad.v1` maps all 64 titles, descriptions, prices, districts, images, posting labels, and structured room count, area, unit floor and building floors. Native records also include repair and apartment-type codes, location, prior price, advertiser class, publisher verification, media order and cover flag, and counts. | Expand the registry to the supported property fields from the raw response. Keep native codes with their JSON types and validate the codebook. Project address and exact coordinates according to location policy and retain first-class prior price observations. |
| BirBir | The `birbir.ad.v1` registry declares 28 fields and shares its serializer across collections and imports. Public Russian and Uzbek details show area, room count, unit floor and total floors. The fixture adapter also keeps price, raw listing attributes, description, location, IDs and images. | Version localized aliases and distinguish structured fields from mentions of complex, renovation, deposit, months and appliances in text. The operator import may supply fewer fields than the fixture collector. The collection and import paths use separate sources. |
| Domtut | Indexed rental cards expose floors like 7/12, monthly price, complex, address, metro distance and walk time, handover month, parking, finish, ceiling, construction and class. The installed `domtut.ad.v1` reader covers new-build projects and layout offers. The local Haven rental entry uses manual import. | Keep parent development, nested project layout options, projected prices and individual unit ads distinct. Preserve `11.2018` at month precision and flag repeated `01.1970` for review. |
| Instagram / Telegram | Registered as future sources. The saved Telegram channel exposes public rental posts. | Accept account or peer identity with post/message IDs, edits, album groups, forwards, caption/text, publication precision and source URLs. A publisher message can contain multiple separate offers. Keep caption-derived facts traceable to text. |

[OLX daily catalog](https://www.olx.uz/nedvizhimost/posutochno_pochasovo/kvartiry/tashkent/), [Domtut rental cards](https://domtut.uz/filter/search?format=rent), [Telegram channel](https://t.me/s/kvartira_bez_posrednika)

Joymee JSON `detail.area_m2` and `pricing.price` are decimal strings. `detail.room_quantity`, `floor_number` and `floors_count` are JSON numbers. All 64 bodies have `detail.apartment_type` (codes 1 and 2) and `detail.repair` (3, 4, 5, 6). All have an address line and 55 have a latitude/longitude pair. Twelve supply a previous price. `mortgage_available` is false across the rental sample. Leave sale-financing interpretation to sale evidence. Mapped currencies are 63 USD and one UZS. The aggregate inventory records observed code frequencies. Meaning remains unverified for repair, apartment type and advertiser type. [Aggregate inventory](sample-profile.json)

## Proposed `wow2.listing.v1` fields

Extend `PostEnvelope` for real-estate listings alongside `wow2.post.v1`. Keep the per-link native snapshot and mapper version. The typed housing model carries the same groups for every source. A project can carry multiple project offers. A multi-property message keeps distinct subjects with their own terms and evidence. Usable typed fields can be projected to query columns without flattening repeating objects or creating a second authoritative record. [Post envelope](../../codebase/harvest.backend-services/packages/Abstractions/Extraction/PostEnvelope.cs), [Event mapper pattern](../../codebase/harvest.backend-services/packages/Module/Extraction/EventSchemaMapper.cs)

| Group | Fields and types |
|---|---|
| Listing identity and intent | Required SCFD post UUID and schema/version. Provider, source, link, category ID/path, external ad/message ID (opaque text), locale, source URL and alternate URLs. Intent enum `offer, wanted, service, promotion, other`. Subject granularity enum `unit, room, building, project, parcel, workspace, other`. Optional distinct property, unit, building, project and duplicate-cluster UUIDs. |
| Publication and lifecycle | Original title and text, original content language and translations. Source published, refreshed, edited, expires, available-from and ended dates with role and precision. First capture, last seen, last checked and observation times. Listing availability enum `available, reserved, rented, sold, withdrawn, suspended, expired, other` separately from source link state. Publisher promotion, badge, verified marker and metrics with as-of and source context. |
| Property classification | Kind enum `apartment, house, room, townhouse, cottage, villa, land, office, retail, warehouse, parking, other`. Market segment `newBuild, resale, other`. Subkind, current use, intended use, tenure, title/registration status, legal/ownership claims. Stage `planned, underConstruction, handedOver, other`. Class `economy, comfort, business, premium, other`. Keep source labels and provider vocabularies. |
| Project and building | Project/complex and phase, building/block/entrance, developer and management organization, local series/model, address or registry ID, attached/detached/form, material and structural system, building counts, total/above-ground/basement floors, lift types/count, construction year or range, commissioning year/date, estimated and actual handover, occupancy/readiness and common-area facilities. |
| Unit and layout | Signed unit floor and optional level end/count, floor display `ground/basement/mezzanine/roof` where stated, building floor count independent. Rooms, bedrooms, living rooms, beds, capacity, full/half baths, total baths or inclusive minimum/maximum. Bath arrangement `separate, combined, other`. Room connection `separate, adjacent, mixed, other`. Style `studio, multilevel, penthouse, compactFamily, other`. Kitchen type, balcony, loggia, terrace, storage and outdoor areas, orientation, view, levels, unit designation. |
| Measurements | Total, usable/living, kitchen, bedroom, lot, balcony and terrace areas, unit frontage, depth, ceiling height and other dimensions. Each has decimal value or range, unit, measured basis and source text. Plot has zoning/use, buildings and independent frontage/depth. |
| Condition and features | Keep maintenance condition, finish stage, renovation grade/date and furnishings independent. Condition `needsRepair, usable, good, other`. Finish `shell, preFinish, finished, other`. Renovation descriptors `euro, designer, average, other` with raw text. Furnishing `unfurnished, partial, furnished, other`. Add scoped features with state `present, absent, unknown`, quantity and qualifiers. |
| Utilities and access | Feature codes by unit/building/complex/nearby. Appliance examples include fridge, washer, stove, air conditioning, TV and internet. Utility examples include gas, electricity, central/individual heating, hot/cold water, sewer, outage/continuity claims and included fees. Building/complex examples include passenger/freight lift, intercom, security, camera, concierge, ramps, wheelchair access, playground, pool, gym and storage. Parking type, spaces, allocation, cost, charging and indoor/outdoor/garage distinction. |
| Offers and money | Independent offers for rent, sale, exchange and other transactions, with active interval and subject ID. Money amount, amount range or starting value, currency, raw price and `fixed, range, from, onRequest, free, negotiable, other` qualifiers. Rental period multiplier and unit `hour, night, day, week, month, year`. Price basis `wholeProperty, room, person, squareMeter, other`. Deposit, commission, advance, operating charges, utilities, parking, inclusions, who pays, percentage/rent multiple basis, refundability and due dates. Financing offers, installment/mortgage terms and explicit sale vs rent. |
| Occupancy and availability | Available calendar precision, min/max term or stay, start/end, guest capacity, rules for pets, smoking and visitors, occupancy type, lease options, check-in/out and booking policy where relevant. Preserve provider text and source assertions. |
| Location and surroundings | Country, region, city, district, mahalla, massif, quarter, street, house/block/entrance/unit and separate published address. Provider place IDs and verified administrative IDs, locale aliases, coordinate point and type/accuracy `exact, approximate, area, other`, elevation if supplied. Nearby facility type, name, distance meters, travel minutes and travel mode. |
| Media | Ordered images, plans, video, audio, virtual tours and documents. Source ID or URL, media kind, file ID if stored, order, caption, declared cover, dimensions, checksum and per-item origin. A photo set is a match signal with source evidence. |
| Publisher and contact | Source account ID, display name/organization, declared platform account class, claimed role `owner, agent, agency, developer, manager, other`, role verification and badges. Contact availability and permitted contact references in access-controlled fields. Keep account category, badge and confirmed role independent. |
| Evidence and quality | Per-field origin and precedence, source capture and key/path, provider label/code/raw value/unit, parser and rule/AI versions, locale, confidence, ambiguity and review state. Unmapped fields remain inspectable. Keep observations of changes to price, terms, status and lifecycle and the resolved value’s link and revision. |

### Example projection

This synthetic example shows a unit, its building, an independent monthly offer and evidence for one field.

```json
{
  "schema": "wow2.listing.v1",
  "intent": "offer",
  "property": {
    "kind": "apartment",
    "marketSegment": "resale",
    "construction": {
      "materials": [
        "brick"
      ]
    },
    "building": {
      "floors": 9
    }
  },
  "unit": {
    "floor": 5,
    "roomCount": 2,
    "layout": {
      "connectivity": "separate"
    },
    "areas": [
      {
        "kind": "total",
        "amount": 51.25,
        "unit": "m2"
      }
    ]
  },
  "offers": [
    {
      "transaction": "rent",
      "price": {
        "amount": 600,
        "currency": "USD",
        "period": {
          "unit": "month",
          "count": 1
        },
        "basis": "wholeProperty"
      }
    }
  ],
  "evidence": [
    {
      "path": "/unit/floor",
      "method": "sourceField",
      "capture": "<capture-id>",
      "nativePath": "/fields/attributes/Этаж",
      "raw": "5"
    }
  ]
}
```

## Types, limits and mapping rules

| Field | Proposed representation | Evidence and guard |
|---|---|---|
| Money | Decimal `numeric(20,6)` plus optional ISO 4217 code and original display marker. | 905 archival prices show `у.е.`; that display marker alone leaves the currency unresolved. Each price stays in its own currency, period and basis. Use a minimum/maximum or starting-price flag for ranges. |
| Areas and lengths | Area `numeric(14,4)` m², heights `numeric(9,4)` m, distances in m and duration in seconds or minutes with named units. | Store the original value and unit. A sotka is 100 m² where explicitly supplied. Bare lot values and mixed ceiling numerals `2.80`, `280` and `2 800` need source/unit evidence. Flag ambiguity and implausibility before assigning a value. |
| Floors and counts | Nullable smallint with signed unit levels, like `-1` for an explicit basement, and separate nonnegative counts. | Parse `5/9` only in a field or text pattern with established ordering. Record floor/building count mismatches. Use quantity ranges or bounds for `2+`. |
| Dates and times | UTC `timestamptz` for exact instants, date/year or bounds plus precision and role for a partial text date. | `1990–2000` is a year interval. `Год постройки/сдачи` is built-or-handover until the source distinguishes it. Keep refresh and capture times independent of publication. |
| Coordinates | WGS 84 longitude/latitude with provenance and optional accuracy in meters. | Validate finite coordinates and their ranges. Mark a district centroid as an area. Keep provider IDs separate from geocoded or administrative IDs. |
| Known values | Closed canonical enum with nullable absent value, and `other` with original label or provider code for a value outside the current dictionary. | Version the raw code/alias tables by source, category and locale. Booleans preserve absent vs explicit false. Unknown codes remain recoverable for replay. |
| Text | NFC text. Proposed 500-character normalized title, 24,000-character description ingest cap for operator forms, 128-character display price, 256-character place/label and URL cap of 2,048 for new submissions. Opaque source IDs use text plus a source-specific validation rule. | Existing BirBir code uses these import/parser bounds, including 30 images and 40 attributes. Observed archive maxima are 70 title and 8,948 description characters; live Joymee maxima are 92 and 999. The legacy 147-character ID stays in evidence. Use text or explicit length/byte checks and return a validation or overflow reason for proposed field caps. Provider input bounds are unverified for these forms. |

Source captures remain immutable within retention. Record source-defined schema and presence, then map a typed value. For a conflict, retain both assertions with method and role. An OLX ad can provide a structured commission flag of no and a fee in the description. Store them as conflicting evidence rather than losing either. Keep `Рестораны, кафе` and `Парк, зелёная зона` as single native amenity labels. Treat review flags separately from the normalized range validation. [BirBir adapter bounds](../../codebase/harvest.backend-services/sources/BirBir/BirBirSourceFields.cs)

RESO provides property vocabulary. JSON Schema defines versioned shape validation, ISO 4217 supplies currency codes, RFC 7946 defines point ordering, and Uzbekistan's administrative registry provides a place ID reference. [RESO data dictionary](https://dd.reso.org/DD2.0/Property/), [JSON Schema 2020-12](https://json-schema.org/draft/2020-12), [ISO 4217 maintenance agency](https://www.six-group.com/en/products-services/financial-information/market-reference-data/data-standards.html), [RFC 7946](https://datatracker.ietf.org/doc/html/rfc7946), [Uzbekistan administrative registry](https://www.lex.uz/mact/-7254182)

## Implementation order

1. Define and validate `wow2.listing.v1` in SCFD. Extend registry entries with native JSON path, type/units, scope, documented values and limits, adapter cap, observed range, source of evidence and mapper coverage. Add OLX and Joymee deterministic mappings first. Retain native codes and preserve meaningful field changes in the capture content projection. Verify replay stability, language aliases, exact decimals, unknowns, and source conflicts against the saved sample.
2. Run the mapper on the retained sample and measure source-present, mapped, invalid, conflicting and unknown values per source, field and category. Add coverage from BirBir captures, Domtut rentals and daily/house/sale categories. Verify codebooks and provider input bounds against publisher documentation or an authorized form test.
3. Have AI propose values only for description-only or unresolved fields such as deposits, leases, project names and prose amenities. Require a text span and extraction/version evidence. Compare precision and coverage against rules-only output on reviewed Russian, Uzbek Latin, Uzbek Cyrillic and mixed-language ads with different offer types. Retain disagreements for review and evaluate whether the field is fit for automatic use.
4. Deliver the validated projection to Haven after replay and compatibility tests. Today Haven's `PropertyType` represents market segment and its classified import rounds prices and can use capture time as a posting fallback. Add an explicit consumer policy for unresolved currency, posting date and floor, and preserve source identities and revisions through the handoff. [Haven listing entity](../../../../../maxs-projects/ventures/saas/10x-ven-haven/engineering/codebase/haven.backend-services/Haven.Supply/Haven.Supply.Domain/Listings/Core/Entities/ListingEntity.cs), [Haven classified mapper](../../../../../maxs-projects/ventures/saas/10x-ven-haven/engineering/codebase/haven.backend-services/Haven.Supply/Haven.Supply.Infrastructure/Classification/FoundationServices/ClassifiedListingMapper.cs)

## First implementation and replay

OLX and Joymee publish a native field schema in source capabilities. Joymee's first reader wrote only a small fixed set of properties to `joymee.ad.v1`, even though the raw capture retained the full detail JSON. The reader now includes repair and apartment-type codes, location, previous price, and the selected publisher and media facts. OLX's capture projection includes stable native facts so a change to the facts can produce a new revision. [Joymee reader](../../codebase/harvest.backend-services/sources/Joymee/JoymeeCaptureReader.cs), [OLX reader](../../codebase/harvest.backend-services/sources/Olx/OlxCaptureReader.cs)

The first `wow2.listing.v1` projection covers the structured facts in those adapters and lives at `extras.listing` on the post. Existing records with materialized housing fields receive the projection on read. The synthetic illustration above describes the proposed broader shape. The implemented schema is generated from the contract types and identifies each mapped value's input path. [Typed contract](../../codebase/harvest.backend-services/packages/Abstractions/Extraction/ListingProjection.cs), [Deterministic mapper](../../codebase/harvest.backend-services/packages/Module/Extraction/ListingSchemaMapper.cs)

| Replay group | Mapped | Rows with data quality flags | Selected results |
|---|---:|---:|---|
| OLX archive | 931/931 | 128 | 931 unit floors, 930 valid building floor counts, 686 construction types. 905 display currencies remain unresolved. |
| Earlier SCFD OLX | 10/10 | 3 | Ten normalized prices with explicit currencies. The flags relate to area labels. |
| Joymee retained JSON | 64/64 | 0 | All 64 mapped rent, floor, building floors and asking currency. Coordinates mapped on 55 and previous price amounts on 12. |

The replay retains unverified Joymee codes for repair, apartment type and advertiser type. The previous amount is available when published. Its currency remains unresolved when the source supplies no prior-price currency. No test inferred a posting term from a price cadence or inferred USD from the OLX display marker. [Replay aggregate](listing-replay.json), [Replay tool and input formats](../../codebase/harvest.backend-services/tools/ListingReplay/ListingReplay.md)

The current apartment card shows the price and key property facts, including the unit floor and building floor count. The detail page adds selectable photos, property fields, offer terms, location, features, the original description and a disclosure for source values needing review. The existing local Haven API returned summaries and a full projection for the ten saved OLX posts.

## Lease extraction trial

A Codex agent received 12 synthetic descriptions in Russian, Uzbek Latin and Uzbek Cyrillic without expected labels. It matched 96 of 96 scalar fields and returned 17 verbatim evidence quotes. Cases include an explicitly waived deposit, a one-month rent multiple, a daily price with a minimum in nights, advance rent without a deposit, and conflicting deposit statements. The local SCFD capture path uses rules. A run through the SCFD AI provider remains pending because neither provider has credentials configured in the local AI bench. [Aggregate trial scores](lease-extraction-trial.json)
