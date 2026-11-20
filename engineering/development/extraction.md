# Extraction (Layer 2)

Layer 2 extraction parses a tagged post's fields into one canonical shape per entity type: `extract-event` for events,
`extract-listing` for listings. It runs only for the (entity type, domain) pairs a subscriber wants, keeps a verbatim
quote behind every date, price and rental term, and stores each answer beside the post. The post takes two things from
an `extract-event` answer: its format as the classification's `kind`, and an estimated date when the post states none.
The entity layer (build plan step C) turns the answers into event and listing entities.
Design: wow-two-ws `ideas/scfd-entity-model.md` #3.1, #4, #9 and build plan step B; tags: `post-tags.md`.
Code: `packages/Module/PostExtraction/` (lead doc `post-extraction.md`).

## Host settings

Extraction needs the shared AI module (`Harvest:Processing:Ai:Enabled`, internal audience only, see `olx-ai.md`) and done
`post-tags` answers (`Harvest:Processing:Tags:Enabled`), then:

| Setting | Default | Meaning |
|---|---|---|
| `Harvest__Processing__Extraction__Wanted__0__EntityType` · `__Domain` | none | a wanted pair: `event` or `listing`, and a taxonomy key; empty queues nothing |
| `Harvest__Processing__Extraction__Models__0` | `anthropic/claude-haiku-5-5` | routes tried in order; a provider without a key leaves the list. Haiku matched Sonnet on the extraction evals at about 1/20 of the cost |
| `Harvest__Processing__Extraction__PromptVersions__extract-event` · `__extract-listing` | none | pins a version; none takes the `production` label, else the newest |
| `Harvest__Processing__Extraction__MaxTextChars` | `6000` | characters of text the prompt receives, and the window quotes must fall in |
| `Harvest__Processing__Extraction__BatchSize` · `PollInterval` · `RetryDelay` · `LeaseDuration` | `10` · `5s` · `15m` · `2m` | worker pace; a failure retries after `RetryDelay` × attempts, at most a day |

- Ocharo's pair: `event` + `style-fashion`. Haven's: `listing` + `real-estate`.
- A post qualifies when its done tags have the pair's entity type and any taxonomy node at or under the key.
- Each active selector subscription adds its (entity type × domain) pairs beside these: its required entity types, or
  both when it names none, crossed with its domain includes (`subscription-selectors.md` § What the selector needs); the
  setting keeps serving pairs no selector names, such as Haven's.
- **Model processing** is a mode per source (`ai-operations.md`): automatic extraction needs every source supplying the
  title and text to be `auto`; `run --post`, a re-run naming posts or listing sources and the extraction that follows them serve `manual`
  too; `off` sends nothing. The worker reads the mode again when it claims a row.
- `Harvest:Worker:Enabled=false` stops this worker with the engine's.

## When it runs

- After tagging: a done `post-tags` answer about the post's current title and text queues the use case of its entity
  type, in the same transaction, when a wanted pair matches and that content has no done answer.
- Section-implied (the listing route): when every source supplying the post's title and text comes from a section that
  declares implied tags (`SourceSectionDefinition.ImpliedTags`: OLX apartments, houses and daily rent declare `listing`
  with `real-estate/apartments`, `real-estate/houses` and `real-estate/real-estate-renting-and-leasing`, and the Joymee and
  BirBir apartment rentals declare `listing` with `real-estate/apartments`), the post write
  stores those tags itself (tier `Rules`, version `Rule`, classifier `rule:section-implied@1`, no `titleEn`) and queues
  no `post-tags` call (`post-tags.md` § Which posts skip it). It then queues the use case of the entity type, in the same
  transaction, when a wanted pair matches (`Wanted:1` is Haven's `listing` + `real-estate`) and the source mode allows it:
  `auto` queues on write, `manual` only a named re-run or `run --post`, `off` nothing. The tags land in the write that
  stores the post, so there is no extra revision. A stored tag naming a taxonomy node that is missing or inactive is
  never written; the post then takes the model path. Posts of any other section keep the rule above, untouched.
- On a content change: a new title or text queues tagging first; once its tags are done, extraction queues for the new
  content. The tag-only revision keeps its content hash and queues nothing.
- On a new wanted pair: `extract backfill` queues stored posts whose current tags match.
- On a re-run (`ai-operations.md`): a re-run of `extract-event` or `extract-listing` queues over the done answer; a
  re-run of `post-tags` queues extraction when the tags extraction reads changed and the demand wants the pair, or when
  the run asked for the tagged type's use case. The done answer for the same content is replaced in place.

## Register the prompts

Prompt text lives in the shared AI database; keep drafts outside the repository. Each draft is a JSON `PromptDraft` with
`useCase` `extract-event` or `extract-listing`, `systemTemplate`, `userTemplate`, optional `modelMatch`, `parameters` and
`note`.

- Variables, both use cases: `{{title}}`, `{{text}}`, `{{sourceCategory}}`, `{{language}}`, `{{dates}}`, `{{place}}`,
  `{{prices}}`, `{{postedAt}}`, and the post's tags `{{entityType}}`, `{{domains}}` (nodes, primary first), `{{about}}`,
  `{{timeframe}}`, `{{titleEn}}`. Any other placeholder is refused; `{{text}}` belongs in `userTemplate`.
- `extract-listing` only: `{{attributes}}`, `{{section}}`, `{{seller}}` (an `extract-event` prompt naming one is refused).
  `{{attributes}}` and `{{seller}}` are OLX-only (`olx.ad.v1`, `olx.md`); `{{section}}` serves every source. They render:
  - `{{attributes}}`: the ad's attribute lines as `label: value`, a badge such as `Бизнес` (no value, or a value that
    repeats the label) as the label alone, in the stored order, at most 40 lines of 200 characters, phones scrubbed.
  - `{{section}}`: for any source, the section key (`apartments-rent`, `houses-rent`, `daily-rent`) when the pipeline's
    scope address is one of the adapter's sections (`ISourceSectionCatalog`), else the pipeline's name.
  - `{{seller}}`: `business` or `private` from the ad's own flag, else its `Бизнес` / `Частное лицо` (`Biznes` /
    `Jismoniy shaxs`) badge, else `unknown`.
  A post that is not an OLX ad (an Afisha one among them) renders `{{attributes}}` and `{{seller}}` empty, and
  `{{section}}` still names its section or pipeline. Eval subjects resolve all three the same way.
- Every prompt's `{{title}}` and `{{text}}` (and the attribute lines) carry no phone: a number becomes `[phone]` before
  the prompt is built (`post-tags.md` § Phone scrub). A name or a contact is never an input; the answer has no field for one.
- `{{postedAt}}` renders as for tagging (`post-tags.md`): an unknown publication date reads the post's first capture.
- The prompt asks for quotes copied exactly from the title, the text or the attribute lines: the check is verbatim, on
  whole tokens, and on the same scrubbed text.

```sh
dotnet HarvestSources.Api.dll extract prompt --use-case event --file /private/path/extract-event-draft.json --by operator
dotnet HarvestSources.Api.dll extract prompt --use-case listing --file /private/path/extract-listing-draft.json --by operator
```

The verb creates the versioned use case with the code schema and `KeepOutputs=true`, then saves an immutable version,
printing only use case, version, content hash and ID. It refuses a draft for the other use case, a draft naming a
variable the use case does not take, and an existing use case in another prompt mode or without `KeepOutputs`. A stored
use case whose schema is an older code schema is brought to the current one in place (route and description kept), so a
schema change such as `extract-listing@2` needs no new use case key: the SQL checks of `harvest_post_extractions`
and the entity tables name `extract-listing`, and earlier versions of the prompt keep their stored outputs.

## Answers

The schemas are code-owned (`PostExtractionDefinitions.EventSchema`, `.ListingSchema`); every field is required, unknown
values are `null` or `unknown`, and every list pick allows `unsure`, which flags the field (scfd-entity-model #9).

| `extract-event` field | Holds |
|---|---|
| `name` | `{en, original}`: the canonical name without year or ordinal, in English and as the source writes it |
| `editionLabel` | the year or ordinal as written (`2026`, `9th`) |
| `occurrences` | `[{starts, ends, precision, certainty: stated · estimated, evidence}]`, ISO dates at `year` · `month` · `day` · `hour` · `minute` |
| `place` · `organizer` | `{venue, city, country}` · the organizer's name |
| `format` | `expo` · `trade-fair` · `runway` · `festival` · `competition` · `conference` · `market` · `exhibition` · `workshop` · `concert` · `other` · `unsure` |
| `access` · `prices` | `free` · `ticketed` · `application` · `unknown` · `[{amount, currency, label, evidence}]` |
| `registration` | `{opens, deadline, link}`, each date `{value, precision, evidence}` |
| `participation` | `exhibitor` · `visitor` · `contestant` · `speaker` · `unsure` |
| `status` | `scheduled` · `cancelled` · `postponed` · `held` · `unsure` |
| `relation` | `{role, changes, status}` (below) |
| `fashion` | only for a primary domain under `style-fashion`: `segments` (`apparel` · `textile` · `accessories` · `jewelry` · `footwear` · `handmade` · `beauty`), `garmentCategories` (English slugs), `exhibitorProfile`, `audience` (`trade` · `consumer` · `mixed` · `unknown`) |

| `extract-listing` field | Holds |
|---|---|
| `title` · `categoryPath` | a short title · the source's own category path, as given |
| `propertyKind` | `apartment` · `house` · `room` · `bed-space` · `commercial` · `land` · `unsure`, or `null` |
| `invalid` | `null` for a usable offer, else `not-a-property` · `multi-listing` |
| `offerType` | `rent` · `sale` · `rent-or-sale` · `exchange` · `rent-to-own` · `unsure` |
| `price` | `null` without an amount, else `{amount, currency: usd · uzs · unsure, period, negotiable, evidence}`; `period` `hour` · `night` · `day` · `week` · `month` · `year` · `unsure` for a rent, else `null` |
| `item` | the unit and its building, for deduplication and filters: `propertyType` (`new-build` · `secondary` · `unsure`), `buildingType` (`brick` · `panel` · `monolithic` · `block` · `wooden` · `unsure`), `yearBuilt`, `rooms`, `layout`, `areaM2` `{total, living, kitchen}`, `floor`, `floors`, `ceilingM`, `bathrooms` `{count, type}`, `renovation`, `building` (the complex's name); each `null` when unknown |
| `furnished` | `yes` · `partly` · `no` · `unknown` · `unsure` |
| `apartmentAmenities` · `nearbyAmenities` | lists over closed sets (24 and 14 values, plus `unsure`) |
| `location` · `landmarks` | `{district, city, address}` · at most 10 of `{type, name, evidence}`, `type` one of 15 closed values or `unsure` |
| `author` · `noAgents` | `{kind: owner · agent · agency · developer · management-company · unknown · unsure}`, no names or contacts · whether the ad refuses agents |
| `relation` | `{role, changes, status}` (below) |
| `rentalTerms` | only for a `rent` offer whose primary domain is under `real-estate`: `deposit`, `minimumStay`, `advanceRent`, `utilities`, `commission`, each `{status: unknown · absent · present · conflict, claims: [{evidence, value}]}` as `rental-terms@2`; `commission` holds `{amount, currency, percent, rentMultiple}` |
| `acceptedTenants` · `excludedTenants` | lists over one closed set (15 values, plus `unsure`) |
| `urgent` | whether the ad presses for a quick deal |

- Every field above is required in the answer; a field with nothing to say is `null`, an empty list or `false`. There is
  no field for a name, a phone or any contact: a contact field is an unknown property and the normalizer removes it
  with a flag. `item.areaM2` has `living` and `kitchen` at or under `total`; `floor` is at or under `floors`; a number
  that breaks the first is dropped with a flag, one that breaks the second is kept with a flag.
- The shape follows the prompt drafts' `schema-v2.json`, with the unit and building facts nested in `item`. Version 1's
  `item.brand`, `model` and `year` and its `service` offer type are gone, and `author.kind` has no `business`.
- Any `unsure` pick, in a field, a list or a claim's currency, adds a flag; a rental term whose claim the version-2
  checks cannot back (an `hour` or `unsure` duration unit, an `unsure` billing) becomes `unknown` with a flag.

- `relation.role`: `announcement` · `update` · `report` · `mention` (#3.1). `changes` applies to an `update` only:
  `dates` · `place` · `price` · `program` · `registration` · `deadline` · `results` · `status`, and `status` names the new
  one: `cancelled` · `postponed` · `sold-out` · `withdrawn`. Whether a later ad is the same author's update is the
  entity layer's call; the model reads the post alone.
- `flags`: `[{field, reason, proposed}]`, the model's own plus the check's.
- Normalizing (`AnswerNormalizer`, before the strict read; both passes run with `AiRunRequest.CheckEnums` false, so the
  AI runner checks the answer's shape and types but leaves every out-of-list value, top-level ones such as `format`
  included, to this repair): a value outside its enum becomes the enum's `unsure` member,
  else its `unknown` one, else `null` for a nullable field (`relation.status`, `invalid`), plus a flag
  `{field: <json path>, reason: "value outside the list", proposed: <the value, 40 characters>}` -- a required enum with
  none of these, a wrong JSON type and a missing field still reject the answer (`failed`, `answer-invalid`). In a list
  (`participation`, `fashion.segments`, `exhibitorProfile`, `relation.changes`, `apartmentAmenities`, `nearbyAmenities`,
  `acceptedTenants`, `excludedTenants`) an item outside the enum is dropped, with
  the flag at `field[index]`, and duplicates go. Case, `-`, `_` and spaces do not matter (`trade fair` reads as
  `trade-fair`), without a flag. An unknown property is removed with a flag `reason: "unknown field"`, no value kept (a
  `phone` or `contacts` an `extract-listing` answer invents goes this way).
  A contact in a rejected value is never kept as `proposed`. The flags ride in the answer's own `flags`, so the checks
  below carry them like the model's; `post-tags` answers take the same repair.

## Evidence

- A quote must appear verbatim, starting and ending on token boundaries, in the title or the part of the text the model
  read — the check `rental-terms@2` uses (`RentalTermEvidence.Quoted`). A quote of an occurrence, price or registration
  date may also come from the rendered `prices`, `dates` or `place` the prompt received (`ExtractionCorpus`), so a value
  the source holds only in its structured fields can be stored. A render is one source: a quote never spans two, and
  one that reads `none` or `unknown` is no source. The `dates` render leaves out an estimated date, so an estimate never
  backs a quote. A rental term may also quote an attribute line of an OLX ad, as the prompt rendered it (`label: value`,
  `ExtractionCorpus.TermSource`): the lines are one more source after the title and text, and a quote never spans the
  two, so `Комиссионные: Нет` backs an `absent` commission.
- A price's quote must also state its amount, thousand, million and billion words included (`50 000 so'm`, `500 тыс`,
  `1,5 mln`, `2 mlrd`); an Uzbek case suffix may follow the scale word (`800 mingdan`, `3 millionga`), and a word that only
  starts like one does not count (`800 mingle`). This holds for a price quoted from `prices` too.
- A stated date's quote must state the date (`RentalTermEvidence.StatesDate`): a number equal to its year (precision
  `year`), or to its month or a month name in Uzbek, Russian or English (`month`), or to its day, its month number or a
  month name (`day` and finer). A range passes on its start or its end. It reads no weekday, relative phrase or spelled-out
  number. Two cases pass without it: an `estimated` occurrence, whose quote is the model's working-out and is only checked
  to exist, and a date equal to a value of the post's `dates` render, which the source states itself (a prompt may tell the
  model to quote the title for such a date).
- A failing occurrence, price or registration date is dropped; a failing rental term becomes `unknown` with no claims
  (`RentalTermEvidence.CheckTerms`, the same per-term rules as version 2). Each gets a flag; the rest of the answer stays.
  The date check adds the flag reason `the quote does not state the date`, with the date as `proposed`.
- Layer 2 reads a conventional unit (`у.е.`, `у. е.`, `y.e.`, `уе`) in a deposit or advance claim as USD, so a claim of USD
  passes and a claim of no or another currency does not. `rental-terms@2` (`RentalTermEvidence.Parse`) keeps its pinned
  rule that a USD claim for `500 у.е.` is rejected (`RentalTermsV2Tests`).
- The utilities cue reads `kommunal`, `komunal`, `kamunal`, `коммунал…` and `комунал…`.
- The `commission` term takes the checks of the other terms (`RentalTermEvidence.CheckListingTerms` maps the listing's
  types onto them): the quote holds a commission cue (`комиссия`, `komissiya`, `commission`, `vositachilik haqi`); a
  percent claim states that percent beside the cue; an amount or rent-multiple claim states it; a claim without a value
  says only that one is charged (`есть`, `bor`); an `absent` claim's quote negates the cue (`Комиссионные: Нет`,
  `без комиссии`).
- The check runs on the text the prompt held: a phone the scrub replaced is `[phone]` in the corpus too, so a quote
  holding a number never matches (`post-tags.md` § Phone scrub).
- Free text is clipped, and a phone, email, handle or messenger link in it is removed with a `contacts` flag, so a stored
  row holds no post text beyond its quotes.

## Verbs

| Verb | Does |
|---|---|
| `extract prompt --use-case <event\|listing> --file <draft \| -> [--by <actor>]` | registers a prompt version |
| `extract run --post <id> [--dry-run]` | extracts one tagged post now with its entity type's use case, whatever the demand, and prints the checked answer; a dry run stores nothing |
| `extract backfill [--since <date>] [--limit N]` | queues stored posts whose current tags match a wanted pair and have no done answer (default limit 1000) |
| `extract report [--since <date>]` | one JSON line per use case: answers by status and relation role, flags by proposed value, queue depth, attempts, cost and p95 latency from the inference log; no post text |

Re-runs with `--prompt-before`, `--since` and `--dry-run`, and eval sets from stored posts: the `processing` verb and
`api/processing` routes (`ai-operations.md`).

## Results

- `harvest_post_extractions`, keyed by `(post_id, content_hash, use_case)`: revision, prompt version and hash, inference
  id, model, `role`, `answer` (the validated shape above), `flags`, `status` `done` · `failed` · `unavailable`, `error`,
  `attempts`, `created_at`, `updated_at`. A failed or unavailable answer keeps no answer and never replaces a done one.
- A done `extract-event` answer about the post's current title and text resolves the post again in its own
  transaction: when the kind or an estimated date changes the post, that is a new revision with `post.changed`. A
  listing answer, or one that changes nothing, writes no revision. Step C reads the done rows for the current content
  hash.
- A done `extract-listing` answer about the post's current content is delivered: `post.extracted` on every active
  subscription that holds the post, with `extras.listingAi` (`contract.md` § Listing delivery). The stored post never
  holds it, and a failed or unavailable answer delivers nothing. The run's `costMicroUsd` rides only on the event; a
  cached answer reads zero.

## Estimated dates (S17)

- A post whose readers state no start gets the answer's first occurrence as a `PostDate` with `Certainty`
  `estimated`: a `start` at the occurrence's precision, an `end` when the occurrence has one and the post states none,
  each quoting the occurrence's evidence as `raw`.
- A stated date always wins: the readers' dates resolve first, so a later capture that states the start replaces the
  estimate on its next resolve. A stated date carries no `certainty` value; it reads as `stated`.
- The answer used is the one about the same title and text as the post's applied tags, so the kind and the estimate
  always come from one reading; a new text keeps the old estimate until its own answers are done.
- `wow2.event.v1` emits `starts.certainty` and `ends.certainty`, `stated` or `estimated` (`../architecture/contract.md`).
- Prompts never read an estimate as the post's own: the `dates` variable of both passes leaves estimated dates out.
- The dedup number signature stays the readers' own dates and prices.
- Eval subjects: `harvest:post:{id}@{revision}` resolves both use cases' variables exactly as the pass builds them, from
  the done tags for that revision's title and text; a subject tagged with the other entity type resolves to nothing
  (`ai-operations.md` § Evals from stored posts).
