# Extraction (Layer 2)

Layer 2 extraction parses a tagged post's fields into one canonical shape per entity type: `extract-event` for events,
`extract-listing` for listings. It runs only for the (entity type, domain) pairs a subscriber wants, keeps a verbatim
quote behind every date, price and rental term, and stores each answer beside the post — never in it. The entity layer
(build plan step C) turns these answers into event and listing entities.
Design: wow-two-ws `ideas/scfd-entity-model.md` #3.1, #4, #9 and build plan step B; tags: `post-tags.md`.
Code: `packages/Module/PostExtraction/` (lead doc `post-extraction.md`).

## Host settings

Extraction needs the shared AI module (`Harvest:Processing:Ai:Enabled`, internal audience only, see `olx-ai.md`) and done
`post-tags` answers (`Harvest:Processing:Tags:Enabled`), then:

| Setting | Default | Meaning |
|---|---|---|
| `Harvest__Processing__Extraction__Wanted__0__EntityType` · `__Domain` | none | a wanted pair: `event` or `listing`, and a taxonomy key; empty queues nothing |
| `Harvest__Processing__Extraction__Models__0` | `anthropic/claude-sonnet-5-5` | routes tried in order; a provider without a key leaves the list |
| `Harvest__Processing__Extraction__PromptVersions__extract-event` · `__extract-listing` | none | pins a version; none takes the `production` label, else the newest |
| `Harvest__Processing__Extraction__MaxTextChars` | `6000` | characters of text the prompt receives, and the window quotes must fall in |
| `Harvest__Processing__Extraction__BatchSize` · `PollInterval` · `RetryDelay` · `LeaseDuration` | `10` · `5s` · `15m` · `2m` | worker pace; a failure retries after `RetryDelay` × attempts, at most a day |

- Ocharo's pair: `event` + `style-fashion`. Haven's: `listing` + `real-estate`.
- A post qualifies when its done tags have the pair's entity type and any taxonomy node at or under the key.
- The pairs stand in for the subscription selector (S09): it will register its own `IPostExtractionDemand`, and the
  setting then goes unused.
- Only sources with **Model processing** on (`AiAllowed`) send words to the model, checked again when the worker runs.
- `Harvest:Worker:Enabled=false` stops this worker with the engine's.

## When it runs

- After tagging: a done `post-tags` answer about the post's current title and text queues the use case of its entity
  type, in the same transaction, when a wanted pair matches and that content has no done answer.
- On a content change: a new title or text queues tagging first; once its tags are done, extraction queues for the new
  content. The tag-only revision keeps its content hash and queues nothing.
- On a new wanted pair: `extract backfill` queues stored posts whose current tags match.

## Register the prompts

Prompt text lives in the shared AI database; keep drafts outside the repository. Each draft is a JSON `PromptDraft` with
`useCase` `extract-event` or `extract-listing`, `systemTemplate`, `userTemplate`, optional `modelMatch`, `parameters` and
`note`.

- Variables, both use cases: `{{title}}`, `{{text}}`, `{{sourceCategory}}`, `{{language}}`, `{{dates}}`, `{{place}}`,
  `{{prices}}`, `{{postedAt}}`, and the post's tags `{{entityType}}`, `{{domains}}` (nodes, primary first), `{{about}}`,
  `{{timeframe}}`, `{{titleEn}}`. Any other placeholder is refused; `{{text}}` belongs in `userTemplate`.
- The prompt asks for quotes copied exactly from the title or text: the check is verbatim, on whole tokens.

```sh
dotnet HarvestSources.Api.dll extract prompt --use-case event --file /private/path/extract-event-draft.json --by operator
dotnet HarvestSources.Api.dll extract prompt --use-case listing --file /private/path/extract-listing-draft.json --by operator
```

The verb creates the versioned use case with the code schema and `KeepOutputs=true`, then saves an immutable version,
printing only use case, version, content hash and ID. It refuses a draft for the other use case and an existing
incompatible use case.

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
| `title` · `offerType` | a short title · `sale` · `rent` · `service` · `unsure` |
| `categoryPath` | the source's own category path, as given |
| `price` | `{amount, currency, period, evidence}`; `period` `hour` · `night` · `day` · `week` · `month` · `year` for a rent |
| `location` | `{district, city, address}` |
| `item` | identity hints for deduplication: `rooms`, `areaM2`, `floor`, `floors`, `building`, `brand`, `model`, `year` |
| `author` | `{kind: owner · agent · business · unknown}`; no names or contacts |
| `relation` | `{role, changes, status}` (below) |
| `rentalTerms` | only for a `rent` offer whose primary domain is under `real-estate`: `deposit`, `minimumStay`, `advanceRent`, `utilities`, each `{status: unknown · absent · present · conflict, claims: [{evidence, value}]}` as `rental-terms@2` |

- `relation.role`: `announcement` · `update` · `report` · `mention` (#3.1). `changes` applies to an `update` only:
  `dates` · `place` · `price` · `program` · `registration` · `deadline` · `results` · `status`, and `status` names the new
  one: `cancelled` · `postponed` · `sold-out` · `withdrawn`. Whether a later ad is the same author's update is the
  entity layer's call; the model reads the post alone.
- `flags`: `[{field, reason, proposed}]`, the model's own plus the check's.

## Evidence

- A quote must appear verbatim, starting and ending on token boundaries, in the title or the part of the text the model
  read — the check `rental-terms@2` uses (`RentalTermEvidence.Quoted`). A price's quote must also state its amount,
  thousand and million words included (`50 000 so'm`, `500 тыс`, `1,5 mln`).
- A failing occurrence, price or registration date is dropped; a failing rental term becomes `unknown` with no claims
  (`RentalTermEvidence.CheckTerms`, the same per-term rules as version 2). Each gets a flag; the rest of the answer stays.
- Free text is clipped, and a phone, email, handle or messenger link in it is removed with a `contacts` flag, so a stored
  row holds no post text beyond its quotes.

## Verbs

| Verb | Does |
|---|---|
| `extract prompt --use-case <event\|listing> --file <draft \| -> [--by <actor>]` | registers a prompt version |
| `extract run --post <id> [--dry-run]` | extracts one tagged post now with its entity type's use case, whatever the demand, and prints the checked answer; a dry run stores nothing |
| `extract backfill [--since <date>] [--limit N]` | queues stored posts whose current tags match a wanted pair and have no done answer (default limit 1000) |
| `extract report [--since <date>]` | one JSON line per use case: answers by status and relation role, flags by proposed value, queue depth, attempts, cost and p95 latency from the inference log; no post text |

## Results

- `harvest_post_extractions`, keyed by `(post_id, content_hash, use_case)`: revision, prompt version and hash, inference
  id, model, `role`, `answer` (the validated shape above), `flags`, `status` `done` · `failed` · `unavailable`, `error`,
  `attempts`, `created_at`, `updated_at`. A failed or unavailable answer keeps no answer and never replaces a done one.
- The post's `base` and its delivery events do not change; step C reads the done rows for the current content hash.
- Eval subjects for the two use cases come with build plan step E.
