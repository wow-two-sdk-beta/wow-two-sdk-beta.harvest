# AI operations

Operator controls over SCFD's AI passes — `post-tags` (`post-tags.md`), `extract-event` and `extract-listing`
(`extraction.md`) — so a prompt can change and its answers be re-run and re-evaluated safely: a model-processing mode
per source, re-runs over named posts, listed sources or every source, and eval sets built from stored posts.
Code: `packages/Module/Reprocessing/` (lead doc `reprocessing.md`) and the spend guard in `packages/Module/Budget/` (lead doc
`budget.md`); routes and verb in `service/HarvestSources.Api/Processing/`; the `sources processing` verb in
`service/HarvestSources.Api/Sources/`.

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

### Setting the mode from the host

`sources processing` reads and sets the mode on the deployed host through Wheelhouse Run, with no listener and no
operator token; it is the verb form of the `PUT` above, for any source of any tenant.

```sh
dotnet HarvestSources.Api.dll sources processing --list [--tenant <slug>]
dotnet HarvestSources.Api.dll sources processing --source <id|family/handle> [--source …] --mode auto
dotnet HarvestSources.Api.dll sources processing --group "Afisha Uzbekistan" --mode manual --tenant ocharo
```

- A **source** is its id or `family/handle` (`site/www.afisha.uz`; case ignored); a **group** is a source group's name
  (`Afisha Uzbekistan`, case ignored; quote a name with spaces). Both repeat and combine; a source named twice is set
  once.
- `--tenant <slug>` limits every selector and the listing to one tenant's containers. A `family/handle` that matches
  sources of several tenants is refused with their ids; name one by id or add `--tenant`.
- Setting prints one line per source on stdout, `<id> <family/handle> <old> -> <new>`; a source already in the mode
  reads `auto -> auto` and is not written. `--list` prints `<id> <family/handle> <mode>` for the sources selected, or for
  every source with no selector, and writes nothing.
- Every selector resolves before any write: an unknown source, an unknown or empty group, an ambiguous
  `family/handle` or an unknown tenant refuses the call (exit `1`) with nothing changed; bad usage exits `2`.
- Ocharo's own sources switch through `ocharo setup --group <publisher> --model-processing <mode>` instead
  (`engineering/deployment/deployment.md` § Provisioning Ocharo on a target); a later setup without the option keeps
  the mode.

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

## Spend guard

One Anthropic credit (about $200) pays every AI call this host makes — Ocharo's `post-tags` and `extract-event`, OLX's
`extract-listing` and `rental-terms@2`, eval runs — so the host slows down, then stops, before the credit runs out. The
guard sums the AI module's inference log over a window and compares it with a limit; it adds no table and no migration.

| Setting (`Harvest:Processing:Budget:`) | Default | Meaning |
|---|---|---|
| `LimitUsd` | none | the hard cap for the window; none turns the guard off (`off`, nothing changes) |
| `SoftShare` | `0.8` | the soft threshold is `LimitUsd × SoftShare`; above 0, at most 1 |
| `CycleStartDay` | `1` | the monthly window starts on this day of the month at 00:00 UTC; 1 to 28 |
| `SinceUtc` | none | ISO 8601 with `Z`: the window is `[SinceUtc, now)` and never resets (a one-off credit); it wins over `CycleStartDay` |
| `RefreshInterval` | `00:01:00` | how long a computed reading is reused; the workers poll every few seconds and never sum per claim |

A value outside its range fails `config check` and a deployed boot (`ValidateOnStart`). `deploy.yml` declares `LimitUsd`,
`SoftShare`, `CycleStartDay` and `SinceUtc`.

| State | When | Workers (`post-tags`, extraction) | Route list for a call | Operator single runs |
|---|---|---|---|---|
| `off` | no `LimitUsd`, or the host composes no inference log | as today | configured `Models` in order | as today |
| `open` | spent under the soft threshold | as today | configured `Models` in order | as today |
| `soft` | spent at or over the soft threshold | as today | only the last configured route (one route: unchanged) | last route |
| `hard` | spent at or over the limit | claim nothing and return `0`: rows stay queued, leases untouched | no model call | refused with `Conflict` |

- **Spent** is the cost of every use case and data scope in the window, evals included.
- **Operator single runs** are `post-tags run`, `extract run` and the `rental-terms@2` stage run. At `hard` the first two
  answer `Conflict` — "The AI spend limit for this window is reached (… of … USD); raise
  Harvest:Processing:Budget:LimitUsd or wait for the next window." — and the stage reports its unavailable outcome with
  `budget-exhausted` in its message, the model not called. `rental-terms@2` takes one pinned route per stage, so `soft` leaves it as it is.
- A batch claimed while the guard was `open` or `soft` and finished after it turned `hard` stores `unavailable` with reason
  `budget-exhausted` and retries after its delay.
- `processing rerun` queues at any state; the workers hold the spend back. Its dry run adds a `budget` object (the reading) to its output.
- A reading the log cannot give (a database error) keeps the last good one; with none, a host that has a `LimitUsd` fails
  closed at `hard` (`readFailed: true`, a warning in the log) until a read succeeds. A host with no limit stays `off`.
- Each change of state is logged once, as a warning for `soft` and `hard`.

```sh
dotnet HarvestSources.Api.dll processing budget
```

| Route | Verb | Does |
|---|---|---|
| `GET api/processing/budget` | `processing budget` | reads the window's spend against the limit; `ApiResponse` envelope on the route, one JSON line from the verb |

Operator only, as the re-run routes (`harvest:manage-storage`); a tenant key gets `403`. The verb writes nothing, exits `0`
for every state including `off`, and works through Wheelhouse Run (no listener, no operator token). The reading holds `state`,
`windowStartUtc`, `windowEndUtc` (monthly windows only), `limitMicroUsd`, `softMicroUsd`, `spentMicroUsd`,
`projectedMicroUsd` (spent ÷ elapsed × the window's length; monthly windows only), `readAtUtc`, the same amounts in USD with
four places (`limitUsd`, `softUsd`, `spentUsd`, `projectedUsd`) and `rows`: `{ useCase, model, attempts, costMicroUsd }` per
use case and model. The route reuses a reading for `RefreshInterval`; `readAtUtc` says when it was summed.

- **Evals are outside the guard.** Eval runs execute inside the AI module (`ModuleEvalsBroker`, the AI API's run routes
  and the eval worker), which the guard does not stop; their cost still counts toward `spent` and can push the host to
  `soft` or `hard`.
- **Escalation.** The code has no flagged-answer escalation to a larger model. When it is added it runs only in `open`.
- **Two targets, one key.** One host has one inference log. Two SCFD targets that share a provider key each count only their
  own, so their `LimitUsd` values must add up to the credit.
- **At `hard`.** Read `processing budget` for the spend and the `rows` that cost it. Then raise `LimitUsd` (the credit has
  room, or was topped up) or wait for the next window: a monthly window resets at `CycleStartDay`, a `SinceUtc` window
  never does, so move `SinceUtc` forward once the credit is renewed. Queued rows resume on the workers' next poll after
  the reading refreshes (within `RefreshInterval`).

## Rollout

1. Deploy: migrations `016-model-processing` and `017-processing-reruns`; every source keeps its old switch as `auto` or
   `off`, and a source row inserted without a mode starts `off`.
2. Set sources that should only run on demand to `manual`.
3. After a prompt change, run `processing rerun … --prompt-before <new version> --dry-run`, read the counts and the
   estimate, then run it without `--dry-run` and follow `processing status`.
4. Build an eval set from stored posts, label its expected answers, and compare prompt versions with the AI API's eval
   runs and report.
5. Before a credit-funded rollout, set `Harvest:Processing:Budget:LimitUsd` (and `SinceUtc` for a one-off credit), then read
   `processing budget`: `open`, the limit and a zero spend.
