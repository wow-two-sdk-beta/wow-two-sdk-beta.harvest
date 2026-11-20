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
- `{{title}}` and `{{text}}` are phone-scrubbed (§ Phone scrub); the text is cut after the scrub.
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

## Phone scrub

Before any prompt is built, `PhoneScrub` replaces a phone number in the title and text with `[phone]`: for `post-tags`,
`extract-event` and `extract-listing` alike (`PostTagsContent.Title` / `.Text`, shared with the extraction content), and in
an OLX ad's `{{attributes}}` lines. The extraction evidence check reads the same scrubbed text, so a quote can never hold
a number the prompt did not.

- It takes a number with a `+` and 7 to 15 digits; a run of 9 to 12 digits with at most two separators (space, dot,
  hyphen, brackets) between digits, when it starts like a Russian `7`/`8` number or `998`; and any such run with a cue word
  (`тел`, `tel`, `phone`, `call`, `whatsapp`, `звон`, `связ`, `номер`, `контакт`) up to 24 characters before it.
- A bare Uzbek mobile goes with no cue word: a 9-digit number whose first two digits are an operator code (20, 33, 50, 55,
  61, 62, 65, 66, 67, 69, 70 to 79, 88, 90, 91, 93, 94, 95, 97, 98, 99), plain or grouped (`901234567`, `90 123 45 67`,
  `(90) 123-45-67`, `901 234 567`, `90.123.45.67`), unless a currency or unit follows within 3 characters (`сум`, `so'm`,
  `sum`, `uzs`, `у.е.`, `$`, `usd`, `м2`, `m²`, `кв`).
- It keeps prices (`1 500 000`, `500000000`, `650 000 000 сум`, `150,000,000`), years, ranges, areas, floors (`3/9`), room
  counts (`2+1`), dates and times, and a lone number that follows a phone (`90 123 45 67 2 комнаты` keeps its `2`).
- It covers phones only: not emails, handles or messenger links (the stored answers clip those, `extraction.md`
  § Evidence). A 9-digit number outside the operator list, and one in a round-price shape (`650 000 000` with no unit),
  stays unless a cue word precedes it.

## Verbs

| Verb | Does |
|---|---|
| `post-tags run --post <id> [--dry-run]` | tags one post now and prints the answer; a dry run stores nothing and raises no revision |
| `post-tags backfill [--since <date>] [--limit N] [--source <id>]` | queues stored posts whose current title and text have no done answer (default limit 1000) |
| `post-tags report [--since <date>]` | one JSON line: answers by status, entity type and primary domain, flags by proposed value, queue depth, attempts, cost and p95 latency from the inference log; no post text |

Comparing the tags with a delivery filter is not a `post-tags` verb: `subscriptions diff --subscription <id|name> --against
<filter key>` judges every post of a selector's container with the selector and with the named filter, counts where they
differ and lists those posts for an operator review (`subscription-selectors.md` § Diff and § Shadow procedure). Cost and
latency stay in `post-tags report`.

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
the tags against the wanted pairs: `Harvest:Processing:Extraction:Wanted` (`extraction.md`) plus the
(entity type × domain) pairs of each active selector subscription, its required entity types, or both when it names none,
crossed with its domain includes (`subscription-selectors.md` § What the selector needs); with no pair, or no done
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
- The verdict is a field of the post, not a delivery gate: a selector subscription (`subscription-selectors.md`) decides
  delivery from the tags themselves (`type:`, `domain:`, `topic:`, `timeframe:`), and a post it cannot judge yet is pending.
  The Ocharo rule filters, `ocharo-fashion-v1` and `ocharo-relevance-v1`, retire once Ocharo's cutover to its selector is
  done (SL9). Until then `ocharo setup` still creates a per-pipeline subscription for `ocharo-fashion-v1`;
  `ocharo-relevance-v1` is registered but has no subscription.
- Tagging off (the default) writes no tags, so no verdict, and a selector subscription holds every post pending.
  Turning tagging off later stops new verdicts; a post keeps its last one, like its tags, until a new answer replaces it.
