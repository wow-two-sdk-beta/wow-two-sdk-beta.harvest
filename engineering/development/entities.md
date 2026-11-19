# Entities

The entity layer binds every extracted post, with a role, to the one real-world thing it is about: an **event** (one
record per recurring event, holding its occurrences, a cadence and a predicted next occurrence) or a **listing** (one
record per offered thing, across authors and sites, each ad an offer). It is the "one entity / one source of truth" a
subscriber searches: Visa Fashion Week Tashkent with every year it ran and each source's posts for that year.
Design: wow-two-ws `ideas/scfd-entity-model.md` #2, #3, #3.1, #4, #7, #9 and build plan step C; answers: `extraction.md`.
Code: `packages/Module/Entities/` (lead doc `entities.md`).

## Host settings

The layer reads stored extraction answers and needs no AI. `service/HarvestSources.Api` composes it on every host.

| Setting | Default | Meaning |
|---|---|---|
| `Harvest__Entities__Enabled` | `false` | a done extraction queues its post's resolution and the worker drains the queue |
| `Harvest__Entities__MinMatchConfidence` | `0.75` | the confidence a candidate needs for a post to bind to it |
| `Harvest__Entities__CandidateFloor` | `0.5` | under it a candidate is none and the post makes a new entity; between it and the minimum the post is flagged |
| `Harvest__Entities__AmbiguityMargin` | `0.05` | a second candidate this close to the best flags the post as ambiguous |
| `Harvest__Entities__EventDateWindowDays` | `10` | two runs whose dates lie this close are one edition |
| `Harvest__Entities__UpdateWindowDays` | `400` | how far a dated update (a postponement) may move an edition |
| `Harvest__Entities__ListingAreaTolerance` · `ListingPriceBand` | `0.05` · `0.2` | the area two ads of one flat may differ by, and the band a price may sit outside a listing's offers |
| `Harvest__Entities__Cadence__AnnualDays` · `SemiannualDays` · `QuarterlyDays` · `MonthlyDays` | `45` · `25` · `15` · `6` | how far a gap may stray from 365, 182, 91 and 30 days |
| `Harvest__Entities__Cadence__PredictMinConfidence` | `0.5` | the cadence confidence a predicted occurrence needs |
| `Harvest__Entities__BatchSize` · `PollInterval` · `RetryDelay` · `LeaseDuration` | `20` · `5s` · `5m` · `2m` | worker pace; a failure retries after `RetryDelay` × attempts, at most a day |

- Startup refuses inverted thresholds (`CandidateFloor` above `MinMatchConfidence`) and out-of-range windows.
- `Harvest:Worker:Enabled=false` stops this worker with the engine's.

## When it runs

- After extraction: a done `extract-event` or `extract-listing` answer queues `entity-resolve` for its post and content
  in the answer's transaction, while the layer is enabled.
- On a content change: the new title or text is tagged, then extracted, then resolved again; the post's binding moves
  with its content, and the old content's relation to another entity drops.
- On enabling the layer over stored answers: `entities backfill` queues every answered post with no relation or flag.

## How a post binds

- **Events**: candidates share the container and a similar name — `name_key` (Russian, Uzbek Latin, Uzbek Cyrillic and
  English forms collapse; years and ordinals drop) or an alias, by pg_trgm similarity. A known different city or tier-1
  domain rules a candidate out; the same city and an overlapping edition each add 0.1. An occurrence matches on dates
  within the window, else on the edition label's year.
- **Roles**: `announcement` creates the entity or an occurrence, or confirms one (a predicted occurrence turns real);
  `update` applies its `changes` — a `cancelled` or `postponed` status and new dates move the occurrence, keeping each
  prior value with the post that changed it in `history`; `report` marks the occurrence held; `mention` only attaches.
  An estimated date never overrides a stated one.
- **Cadence**: from two or more stated starts — ~365 days annual, ~182 semiannual, ~91 quarterly, ~30 monthly, else
  irregular — with a confidence; one `predicted` occurrence follows the last real one and is replaced when a real one
  arrives.
- **Listings**: the same post or the same `(source_id, external_key)` is an `update`. Otherwise item identity scores the
  ad — offer type, category path, city, district, rooms, area within 5%, floor, building, brand, model, year and the
  price band — and the same author adds to it. The same author's ad is an `update`; another author's is an
  `announcement` with its own offer (author kind, price, link, source, first seen). `facts.priceSpread` keeps each
  author's latest price per currency and period.
- **Unsure**: a best match under `MinMatchConfidence`, or two too close to call, binds nothing and records a flag with
  the candidate for review; a false merge is worse than a duplicate. `entities report` counts the open flags.
- **Keep-apart**: a split pair is never merged, and resolution never binds a post to an entity kept apart from the one
  it is already bound to.

## Verbs

Each prints one JSON line, except `show` and `search`, which print one JSON document; none prints post text.

```bash
dotnet HarvestSources.Api.dll entities resolve --post <post-id> --dry-run
dotnet HarvestSources.Api.dll entities resolve --post <post-id>
dotnet HarvestSources.Api.dll entities backfill --since 2026-11-01 --limit 1000
dotnet HarvestSources.Api.dll entities show --id <entity-id>
dotnet HarvestSources.Api.dll entities search --q "Visa Fashion Week Tashkent" --type event --domain style-fashion
dotnet HarvestSources.Api.dll entities merge --from <duplicate-id> --into <entity-id>
dotnet HarvestSources.Api.dll entities split --a <entity-id> --b <other-id>
dotnet HarvestSources.Api.dll entities report
```

- `resolve` prints the outcome per answer: `created`, `bound` or `unsure`, the entity, occurrence, role, changes,
  confidence and, when unsure, the reason (`weak-match`, `ambiguous`, `no-name`). A dry run rolls every write back.
- `show` prints the entity, its occurrences by year (history included) and each relation's post id, role, source and,
  for a listing, its offer; a merged id reads as the entity it was merged into.
- `search` matches name and alias similarity and returns each entity with the years it ran and its predicted next start.
- `merge` refuses a merged entity, another container or type, and a kept-apart pair; `split` refuses a merged entity.
- Exit codes: 0 done, 1 refused, 2 invalid arguments.

## Rollout

1. Deploy with `Harvest__Entities__Enabled=false`; migration `014-entities` adds the tables.
2. Run `entities resolve --post <id> --dry-run` on a few extracted posts and read the outcomes.
3. Set `Harvest__Entities__Enabled=true`, run `entities backfill`, then `entities report` for counts and open flags.
4. Review flags with `entities show` on each candidate; correct with `merge` or `split`, then `resolve` the post again.

## Not yet

- Reads for subscribers and the entity change feed are step D; nothing here is delivered.
- One relation per extraction answer: a digest post binds the one entity its answer names until extraction returns a
  list.
- A post merged by post dedup keeps its relations under its own id.
- A re-captured listing whose price changed goes through tagging and extraction again; the design's revision-diff
  update without a model call is not built.
