# Post tags (Layer 1)

The `post-tags` pass tags every post revision whose title or text changed: entity type, IAB domain and subdomain, open
English topics, and routing fields. Done tags become a new post revision that subscriptions receive as `post.changed`.
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

## Results

- `harvest_post_tags`, keyed by `(post_id, content_hash)`: prompt version and hash, inference id, model, the validated
  fields and flags, `status` `done` · `failed` · `unavailable`. A failed or unavailable answer never replaces a done one.
- The post carries the newest done answer: tags tier `SmallModel` with keys `type:{entityType}`, `domain:{node}` and
  each ancestor node, `topic:{slug}`; `extras.postTags.titleEn`. A subscription filter `tag:domain:style-fashion`
  therefore matches a post tagged `style-fashion/womens-fashion`.
- Unsure values and unknown nodes gather as flags; `report` counts them by proposed value for the prompt or taxonomy fix
  (scfd-entity-model #9).
- Eval subjects: `harvest:post:{id}@{revision}` resolves to the same variables for the AI eval bench.
