# AI operations

Operator controls over SCFD's AI passes — `post-tags` (`post-tags.md`), `extract-event` and `extract-listing`
(`extraction.md`) — so a prompt can change and its answers be re-run and re-evaluated safely: a model-processing mode
per source, re-runs over named posts, listed sources or every source, and eval sets built from stored posts.
Code: `packages/Module/Reprocessing/` (lead doc `reprocessing.md`); routes and verb in `service/HarvestSources.Api/Processing/`.

## Model processing per source

A source's **Model processing** is a mode (`ModelProcessingMode`, migration `016`), set in the console's source settings
or with `PUT api/sources/{id}` `{ "modelProcessing": "manual" }`:

| Mode | Queues on its own | Re-runs naming posts or listing sources · eval items | Scheduled Process stage · Process stage started by hand |
|---|---|---|---|
| `off` | no | refused | refused · refused |
| `manual` | no | yes | refused · yes |
| `auto` | yes, on every write | yes | yes · yes |

- A post takes the strictest mode of the sources supplying its title and text.
- Every worker reads the mode when it claims a row, not when the row was queued: a row nobody named needs `auto`; a
  row an operator named — by post, or by listing its source — needs any mode but `off`. Switching a source to `manual` or `off` stops its queued automatic
  work; a call already in flight may finish.
- `post-tags run --post` and `extract run --post` name the post: they run under `manual`, and the extraction or entity
  resolution that follows them does too.
- The OLX `rental-terms@2` processor follows the same mode: `manual` keeps **Run now** and refuses a scheduled Process
  run, enabling an automatic Process schedule, and a scheduled run queued before the switch.
- Migration `016` maps the old switch: `aiAllowed` `true` became `auto`, `false` became `off`; the column then
  defaults to `off`, so a raw insert never starts sending posts to a model (the code always writes the mode). The wire keeps
  `aiAllowed` (true unless `off`); a client that sends only `aiAllowed` sets `auto` or `off`, and `modelProcessing`
  wins when both are sent — an update whose two switches disagree is refused. The stored `ai_allowed` column stays true
  only for `auto`, so code before `016` never queues a manual source.

## Re-runs

A re-run queues posts through the passes' own work queue (`harvest_post_work`), so the workers, the retries and the
source modes apply as to any queued post. It needs shared AI (`Harvest:Processing:Ai:Enabled`); one that names
`post-tags` needs `Harvest:Processing:Tags:Enabled` as well.

```sh
dotnet HarvestSources.Api.dll processing rerun --sources <id>,<id> --use-case post-tags --prompt-before 3 --dry-run
dotnet HarvestSources.Api.dll processing rerun --posts <id>,<id> --by operator
dotnet HarvestSources.Api.dll processing rerun --all-sources --use-case extract-event --since 2026-11-01
dotnet HarvestSources.Api.dll processing status --run <run-id>
```

| Route | Verb | Does |
|---|---|---|
| `POST api/processing/reruns` | `processing rerun` | queues a re-run (`202`, with `runId` and its status route), or counts and prices it (`dryRun`, `200`) |
| `GET api/processing/reruns/{runId}` | `processing status --run` | the counts the run queued with, and per work kind the rows still waiting, running or retrying with their last errors |

Both are operator only (`harvest:manage-storage`: `containers:manage` held by the operator); a tenant key gets `403`.

- **Scope**, exactly one: `postIds` (`--posts`, at most 1,000), `sourceIds` (`--sources`, at most 100) or `allSources`
  (`--all-sources`).
- **Filters**: `sinceUtc` (`--since`) keeps posts whose last change is that late; `promptBefore` (`--prompt-before`)
  keeps posts whose done answer for the current title and text came from an older prompt version; `useCases`
  (`--use-case a,b`) names `post-tags`, `extract-event`, `extract-listing` — none re-runs all three.
- **Which posts**: current posts with a title or text; for extraction, posts whose done tags name the use case's entity
  type, and, over listed or all sources, a pair the demand wants (`Harvest:Processing:Extraction:Wanted`) — named posts
  extract whatever the demand. An extraction in the same run leaves out the posts it re-tags: theirs follows the tags.
- **Modes**: named posts and listed sources are explicit operator choices and run under `manual`; only a run over all
  sources skips a `manual` source's posts (`manual`). A post with an `off` source is refused in every scope, and
  `refused` counts it. A row a worker holds is skipped (`inFlight`); a named post the
  use case cannot take — missing, merged, empty, untagged — is skipped (`notEligible`).
- **Dry run**: the same queueing in a transaction that is rolled back, so the counts are what a run would queue;
  nothing is written. Each use case is priced from the inference log: the newest production calls of the last 30 days
  on the use case's first configured route, summed per post and averaged (`costPerPostMicroUsd`, `costBasis`), times
  the queued posts. An answer the AI module already holds for the same prompt and input costs nothing, so the estimate
  is an upper bound.
- **Answers**: a done answer for the same content is replaced in place (`harvest_post_tags` per post and content,
  `harvest_post_extractions` per post, content and use case); a failed or unavailable one never replaces it. The AI
  module's inference log keeps every answer (`KeepOutputs`), the history of each re-run.
- **Downstream**: new tags raise a revision with `post.changed` and their classification (`post-tags.md`). When the tags
  extraction reads (entity type, domains, about, timeframe, English title) changed and the demand wants the pair, or
  the run asked for the tagged type's use case, extraction queues over its done answer, carrying the run. A done
  extraction resolves the post's kind and estimated dates, then queues entity resolution (`entities.md`) with the run.
  `status` follows all three kinds until the queues hold nothing of the run (`done`).
- **Records**: `harvest_processing_runs` (`017`) keeps each run's scope, filters, who asked and its counts; never post
  text.

## Evals from stored posts

The source host mounts the AI module's own API (`MapAiApi`, routes under `api/ai/`) while shared AI is on: use cases,
prompts, labels, prices, the inference log, eval sets, items, runs (models × prompt versions, queued as a matrix),
ratings and reports. Each route keeps its `ai.*` scope (`ai.read`, `ai.run`, `ai.prompts-manage`, `ai.evals-manage`);
an SCFD operator holds them all, a tenant key none. Without shared AI the routes are not mapped.

`harvest:post:{id}@{revision}` subjects resolve the inputs of all three use cases exactly as the passes build them:
`post-tags` its variables and taxonomy, `extract-event` and `extract-listing` the post fields with prices and the done
tags for that revision's title and text. A subject whose tags name another entity type, or whose source is `off`,
resolves to nothing.

```sh
dotnet HarvestSources.Api.dll processing eval-set --set afisha-tags --use-case post-tags --sources <id> --sample 50 --seed 7 --expected current
```

| Route | Verb | Does |
|---|---|---|
| `POST api/processing/eval-sets` | `processing eval-set` | adds a seeded sample of stored posts to an eval set, creating it when missing |

- `sample` 1–500 and `seed`: the same seed over the same posts draws the same sample; posts already in the set are
  skipped, so a second call adds only new ones.
- `expected` `empty` starts each item at `{}`; `current` takes the post's current done answer (posts without one are
  not drawn), labelled by `harvest:current-answer`. Both are tagged `unreviewed` until someone relabels them through
  `PUT api/ai/evals/items/{id}/expected`.
- A new set takes `scoreFields` and `note`; an existing set of another use case is refused.
- Then run models × prompt versions and read the report through `api/ai/evals/…` as for any set.

## Rollout

1. Deploy: migrations `016-model-processing` and `017-processing-reruns`; every source keeps its old switch as `auto` or
   `off`, and a source row inserted without a mode starts `off`.
2. Set sources that should only run on demand to `manual`.
3. After a prompt change, run `processing rerun … --prompt-before <new version> --dry-run`, read the counts and the
   estimate, then run it without `--dry-run` and follow `processing status`.
4. Build an eval set from stored posts, label its expected answers, and compare prompt versions with the AI API's eval
   runs and report.
