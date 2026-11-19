# Post tags (Layer 1)

The `post-tags` pass tags every post revision whose title or text changed: entity type, IAB domain and subdomain, open
English topics, and routing fields. Done tags, with the relevance verdict they give, become a new post revision that
subscriptions receive as `post.changed`.
Design: wow-two-ws `ideas/scfd-entity-model.md` #4, #6, #9 and build plan step A; fields: `ideas/scfd-ai-layers.md` #6.
Code: `packages/Module/Tagging/` (lead doc `tagging.md`).

Taxonomy: IAB Tech Lab Content Taxonomy 3.1, licensed CC BY 3.0, keyed by our own slugs with English labels.

## Host settings

Tagging needs the shared AI module (`Harvest:Processing:Ai:Enabled`, internal audience only, see `olx-ai.md`), then:

| Setting | Default | Meaning |
|---|---|---|
| `Harvest__Processing__Tags__Enabled` | `false` | post writes queue tagging and the worker drains it |
| `Harvest__Processing__Tags__Models__0` | `anthropic/claude-haiku-5-5` | routes tried in order; a provider without a key leaves the list |
| `Harvest__Processing__Tags__MaxTextChars` | `4000` | characters of text the prompt receives |
| `Harvest__Processing__Tags__PromptVersion` | none | pins a version; none takes the `production` label, else the newest |
| `Harvest__Processing__Tags__BatchSize` · `PollInterval` · `RetryDelay` · `LeaseDuration` | `10` · `5s` · `15m` · `2m` | worker pace; a failure retries after `RetryDelay` × attempts, at most a day |

- Only sources with **Model processing** on (`AiAllowed`) send words to the model: every source supplying the post's
  title and text must allow it, checked again when the worker runs.
- `Harvest:Worker:Enabled=false` stops this worker with the engine's.

## Register the prompt

Prompt text lives in the shared AI database; keep drafts outside the repository. The draft is a JSON `PromptDraft` with
`useCase: "post-tags"`, `systemTemplate`, `userTemplate`, optional `modelMatch`, `parameters` and `note`.

- Variables: `{{title}}`, `{{text}}`, `{{sourceCategory}}`, `{{language}}`, `{{dates}}`, `{{place}}`, `{{postedAt}}`
  and `{{taxonomy}}`; any other placeholder is refused. `{{taxonomy}}` belongs in `systemTemplate`, so the provider's
  prompt cache holds it; `{{text}}` in `userTemplate`.
- `postedAt` is the publication date as `yyyy-MM-dd`, or `unknown`; the prompt judges `timeframe` against it.
- The answer schema is code-owned (`PostTagsDefinition.OutputSchema`): `entityType` (`event` · `listing` · `none` ·
  `unsure`), `domains` (`[{node, primary}]`, taxonomy keys), `topics`, `about`, `timeframe` (`upcoming` · `ongoing` ·
  `past` · `evergreen` · `unsure`), `items`, `language`, `confidence` (0–1), `titleEn`, `flags`
  (`[{field, reason, proposed}]`).

```sh
dotnet HarvestSources.Api.dll post-tags prompt --file /private/path/post-tags-draft.json --by operator
```

The verb creates the versioned use case with the code schema and `KeepOutputs=true`, then saves an immutable version,
printing only use case, version, content hash and ID. It refuses an existing incompatible use case.

## Verbs

| Verb | Does |
|---|---|
| `post-tags run --post <id> [--dry-run]` | tags one post now and prints the answer; a dry run stores nothing and raises no revision |
| `post-tags backfill [--since <date>] [--limit N] [--source <id>]` | queues stored posts whose current title and text have no done answer (default limit 1000) |
| `post-tags report [--since <date>]` | one JSON line: answers by status, entity type and primary domain, flags by proposed value, queue depth, attempts, cost and p95 latency from the inference log; no post text |
| `post-tags relevance [--since <date>]` | the shadow report (S15): one JSON line comparing `ocharo-fashion-v1`'s verdict with the tags' verdict for Ocharo's pair; no post text |

## Results

- `harvest_post_tags`, keyed by `(post_id, content_hash)`: prompt version and hash, inference id, model, the validated
  fields and flags, `status` `done` · `failed` · `unavailable`. A failed or unavailable answer never replaces a done one.
- The post carries the newest done answer: tags tier `SmallModel` with keys `type:{entityType}`, `domain:{node}` and
  each ancestor node, `topic:{slug}`; `extras.postTags.titleEn`. A subscription filter `tag:domain:style-fashion`
  therefore matches a post tagged `style-fashion/womens-fashion`.
- Unsure values and unknown nodes gather as flags; `report` counts them by proposed value for the prompt or taxonomy fix
  (scfd-entity-model #9).
- Eval subjects: `harvest:post:{id}@{revision}` resolves to the same variables for the AI eval bench.

## Classification (S16)

The same resolve that writes the tags writes the post's `classification`: one revision, one `post.changed`. It judges
the tags against the wanted pairs (`Harvest:Processing:Extraction:Wanted`, `extraction.md`); with no pair, or no done
answer, the post keeps exactly its readers' envelope.

| Tags | `verdict` | `reason` |
|---|---|---|
| entity type `unsure` | `uncertain` | `entity type unsure` |
| the entity type and a node at or under a wanted pair | `relevant` | `{entityType} under {domain}` |
| entity type `none` | `irrelevant` | `entity type none` |
| an event or listing with no known node (the pick was unsure or unknown) | `uncertain` | `primary domain unsure` |
| known nodes off every pair | `irrelevant` | `off every wanted pair` |

- `classifier` `model:{provider}/{model}@post-tags:{promptVersion}`; `confidence` is the answer's.
- `kind` is the `format` of a done `extract-event` answer about the same title and text (`runway`, `trade-fair`, …), else
  the entity type; an unsure entity type names none. An extraction answer that changes the kind raises its own revision.
- `wow2.event.v1` carries it as `classification` and top-level `kind`; Events' `payload` classifier reads it.
- Filter `ocharo-relevance-v1` (`sources/Module/Ocharo/OcharoRelevanceFilter.cs`) passes what `ocharo-fashion-v1`
  passes, plus a `relevant` verdict at 0.5 or above on a post whose model tags are `type:event` and
  `domain:style-fashion`; the tag check keeps Haven's `listing` + `real-estate` pair out. No subscription uses it yet:
  Ocharo's new subscription waits on the selector (S09), and `ocharo setup` still provisions `ocharo-fashion-v1`.
- Tagging off (the default) writes no tags, so no verdict: the relevance filter selects exactly what the rules select.
  Turning tagging off later stops new verdicts; a post keeps its last one, like its tags, until a new answer replaces it.

## Shadow report (S15)

`post-tags relevance` counts the current posts whose sources allow the model and that changed since `--since` (seven days
back by default), split by `ocharo-fashion-v1`'s verdict (rules yes, rules no) and by the tags' verdict for Ocharo's
pair `event` + `style-fashion` — the same mapping as the classification:

```json
{"sinceUtc":"…","rules":"ocharo-fashion-v1","wanted":["event:style-fashion"],"posts":4,
 "rulesYes":{"match":1,"noMatch":0,"unsure":0,"notTagged":0},"rulesNo":{"match":1,"noMatch":1,"unsure":0,"notTagged":1},
 "rulesUndecided":0,"invalidAnswers":0,"taggedPosts":3,"attempts":3,"costMicroUsd":0,"costPerPostMicroUsd":0,
 "p95LatencyMs":0,"latencySample":3}
```

- `notTagged`: the current title and text have no done answer yet. `rulesUndecided`: the filter threw.
- `invalidAnswers`: answers stored since `--since` as `schema-invalid` or `answer-invalid` that no done answer replaced.
- `costPerPostMicroUsd`: the use case's cost since `--since` over the counted posts with a done answer; attempts and
  p95 latency come from the inference log, as `report`'s do.
- The rules verdict is the registered filter's own `Matches`, run on the stored post; nothing is written.
- Ocharo's sources are provisioned with Model processing off: turn it on for the sources to compare (Afisha first) and
  enable tagging, or the report counts none of their posts.
