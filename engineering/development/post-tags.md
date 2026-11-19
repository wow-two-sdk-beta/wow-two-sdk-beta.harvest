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

- **Model processing** is a mode per source (`ai-operations.md`): post writes and `backfill` queue only posts whose every
  source supplying the title and text is `auto`; `run --post` and a re-run naming posts or listing sources also serve `manual`; `off`
  sends nothing. The worker reads the mode again when it claims a row.
- `Harvest:Worker:Enabled=false` stops this worker with the engine's.

## Register the prompt

Prompt text lives in the shared AI database; keep drafts outside the repository. The draft is a JSON `PromptDraft` with
`useCase: "post-tags"`, `systemTemplate`, `userTemplate`, optional `modelMatch`, `parameters` and `note`.

- Variables: `{{title}}`, `{{text}}`, `{{sourceCategory}}`, `{{language}}`, `{{dates}}`, `{{place}}`, `{{postedAt}}`
  and `{{taxonomy}}`; any other placeholder is refused. `{{taxonomy}}` belongs in `systemTemplate`, so the provider's
  prompt cache holds it; `{{text}}` in `userTemplate`.
- `postedAt` is the publication date as `yyyy-MM-dd`; with no publisher date it reads `unknown; first seen yyyy-MM-dd`,
  the post's first capture, since the post was out by then, else `unknown`. The prompt judges `timeframe` against it.
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
| `post-tags relevance --filter <key> --wanted <type:domain[,…]> [--since <date>]` | the shadow report (S15): one JSON line comparing a registered delivery filter's verdict with the tags' verdict for the named pairs; no post text |
| `post-tags relevance --filter … --wanted … --disagreements [--since <date>] [--limit N]` | an operator review: one JSON line listing the posts the rules and the tags disagree on; carries scrubbed titles -> § Disagreements |

Re-runs over named posts, listed sources or every source, with `--prompt-before`, `--since` and `--dry-run`, and eval sets
from stored posts: the `processing` verb and `api/processing` routes (`ai-operations.md`).

## Results

- `harvest_post_tags`, keyed by `(post_id, content_hash)`: prompt version and hash, inference id, model, the validated
  fields and flags, `status` `done` · `failed` · `unavailable`. A failed or unavailable answer never replaces a done one.
- The post carries the newest done answer: tags tier `SmallModel` with keys `type:{entityType}`, `domain:{node}` and
  each ancestor node, `topic:{slug}`, `timeframe:{value}` (every value, `unsure` included); `extras.postTags.titleEn`. A subscription filter `tag:domain:style-fashion`
  therefore matches a post tagged `style-fashion/womens-fashion`.
- `wow2.event.v1` projects `extras.postTags.titleEn` as `titles` (`machine: true`, English), so a subscriber reads it
  beside the source title: `../architecture/contract.md` § Source host.
- Unsure values and unknown nodes gather as flags; `report` counts them by proposed value for the prompt or taxonomy fix
  (scfd-entity-model #9).
- A re-run's done answer replaces the done one for the same content in place; the inference log keeps both.
- Eval subjects: `harvest:post:{id}@{revision}` resolves to the same variables for the AI eval bench, for `post-tags` and
  both extraction use cases (`ai-operations.md` § Evals from stored posts).

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

`post-tags relevance --filter <key> --wanted <type:domain[,…]>` counts the current posts whose sources are all `auto`
and that changed since `--since` (seven days back by default), split by the named filter's verdict (rules yes, rules no)
and by the tags' verdict for the named pairs — the same mapping as the classification. The verb names no product's
filter; the example compares `--filter ocharo-fashion-v1 --wanted event:style-fashion`:

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
- Ocharo's sources are provisioned with Model processing `off`: set the sources to compare (Afisha first) to `auto` and
  enable tagging, or the report counts none of their posts.

## Disagreements

`post-tags relevance --filter … --wanted … --disagreements [--since <date>] [--limit N]` prints one JSON line instead of the counts, over the same
posts, rules verdict and tag verdict as the shadow report. It is an **operator review that prints post titles** — the
only `post-tags` output that does; read it where the report's counts would be read, never forward it to a subscriber.

```json
{"rules":"ocharo-fashion-v1","wanted":["event:style-fashion"],"items":[
 {"postId":"…","source":"site/afisha.example","title":"Evening at the gallery","titleEn":"An evening at the gallery",
  "rules":"no","tags":"match","entityType":"event","primaryDomain":"style-fashion","domains":["style-fashion"],
  "topics":["fashion-show"],"about":"young designers present a collection","timeframe":"upcoming","confidence":0.8}]}
```

- Listed: a post the rules pass whose tags say `noMatch` or `unsure` (`rules` `yes`), and a post the rules reject whose tags
  say `match` or `unsure` (`rules` `no`). Posts that agree, posts with no done answer and posts the filter threw on are
  not listed.
- Order: `rules` `yes` first, then by post id; `--limit N` keeps the first N (default 50, at most 1,000). `--limit` without
  `--disagreements` is refused.
- `source` is `{family}/{handle}` of the source supplying the post's title, else its text. `primaryDomain` is the primary
  node's key and `domains` every node key the answer kept; `tags` is the verdict the classification would give.
- `title` has phone numbers (9 or more digits), emails, handles and messenger links replaced by `…` — the patterns
  extraction uses to drop contacts from free text (`PostExtractionValidation.Scrub`) — then is clipped to 140 characters; a
  date followed by a time (`12.12.2026 18:00`) holds 9 digits and is scrubbed like a phone number, as extraction would drop it.
  `titleEn`, `about` and `topics` come from the stored answer, which already holds no contact.
- Wheelhouse's one-off command keeps the last output line whole only up to 16 KiB; an item runs from about 350 bytes to
  well over 600 with a full-length title and answer, so the default 50 can overflow — lower `--limit` when the line is cut.
