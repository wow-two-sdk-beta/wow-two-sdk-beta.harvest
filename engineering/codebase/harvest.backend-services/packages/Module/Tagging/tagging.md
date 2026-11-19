# Tagging/

Layer 1 `post-tags` (wow-two-ws `ideas/scfd-entity-model.md` build plan step A): every post's title and text get AI
tags subscriptions and later steps rely on — entity type, IAB domains, open English topics and a few routing fields.

| Implements | Over |
|---|---|
| `IPostTagsBroker` (this lane's own seam: run, backfill, report, the relevance shadow report, the worker's tick) · `IEvalSubjectSource` for `harvest:post:{id}@{revision}`, resolving `post-tags`, `extract-event` and `extract-listing` inputs | the shared AI contract `WoW2.Sdk.Beta.Be.Ai.Abstractions` (`IAiRunner`, `IPromptsBroker`, `IInferencesBroker`); the Posts lane's resolve and the Delivery lane's `post.changed` append |

- Owns: `harvest_taxonomy_nodes` (011), `harvest_post_tags` and `harvest_post_work` (012).
- Taxonomy: IAB Tech Lab Content Taxonomy 3.1 (CC BY 3.0) — its 34 subject domains and their 623 descendants, keyed by
  our slug path (`style-fashion/womens-fashion`), English labels; `Events`, `Genres` and `Sensitive Topics` stay out.
  `tools/iab-taxonomy-seed.py` generates the seed once from the 3.1 TSV; the TSV is not committed. Our own nodes join
  as `extension` rows.
- Queue: a post write whose title or text has no `done` answer queues `post-tags` in `harvest_post_work`, inside the
  write's transaction — only when `Harvest:Processing:Tags:Enabled` is set, the host composes the AI module, and every
  source supplying the title and text is in `auto` mode (`PostTagsStore.AutoSql`). `post-tags backfill` queues stored
  posts the same way. A re-run's row (Reprocessing lane) carries `run_id` and `explicit`; an explicit row (named posts,
  listed sources, `run --post`) also runs under `manual`. The worker reads the strictest mode (`ModeSql`) when it claims a row.
- Re-run cascade: a done answer for a row with a run compares what extraction reads (entity type, domains, about,
  timeframe, English title) before and after, and queues extraction forced, with the run, when it changed and the demand
  wants the pair, or when the run asked for the tagged type's use case (over sources, for a pair the demand wants) (`ModulePostBroker.QueueRerunExtractionAsync`).
- Loop guard: `content_hash` is the SHA-256 of title and text only, so the tag-only revision the write-back creates
  keeps its hash, finds its own `done` answer and queues nothing.
- Call: use case `post-tags`, versioned, `KeepOutputs` true, the code-owned schema (`PostTagsDefinition.OutputSchema`);
  variables `title`, `text` (cut to `MaxTextChars`), `sourceCategory`, `language`, `dates`, `place`, `postedAt`
  (`yyyy-MM-dd` or `unknown`) and `taxonomy` (`key — label` lines in key order, for the system template so provider
  caching holds). Routes come from `Harvest:Processing:Tags:Models`, default `anthropic/claude-haiku-5-5`.
- Validation: an unknown node becomes a `domains` flag carrying the proposed value, never a stored node; one known node
  is primary; topics become English slugs or a `topics` flag; an `unsure` pick always carries a flag. Before this,
  `PostTagsDefinition.Parse` runs the PostExtraction lane's `AnswerNormalizer`: an enum value the schema lacks becomes
  `unsure` with a flag, an unknown property is removed with a flag, a wrong JSON type still fails the answer.
- Outcomes: `done` stores the answer and resolves the post again; `failed` (refused, schema-invalid, provider errors)
  and `unavailable` (no runner, use case or prompt; a provider without key) store the status, write no tags and retry
  after `RetryDelay` × attempts, capped at a day.
- Write-back: the post's resolve overlays the stored answer on every resolve — tags tier `SmallModel`, keys
  `type:{entityType}` (none for `unsure`), `domain:{node}` plus each ancestor, `topic:{slug}`, version
  `post-tags@{promptVersion}`, score = confidence — and `extras.postTags.titleEn`. Mapped tags keep their own tiers.
  A changed `base` raises the revision and appends `post.changed`; identical tags write nothing.
- Classification: the same overlay writes `PostTagsClassification`'s verdict for the wanted pairs (the PostExtraction
  lane's demand): `relevant` on a matching pair, `irrelevant` for `none` or nodes off every pair, `uncertain` for an
  unsure entity type or no known node; classifier `model:{model}@post-tags:{version}`, the answer's confidence, `kind`
  the `extract-event` format about the same content, else the entity type. No pair or no answer: no classification.
- Relevance report: `RelevanceAsync` counts the current AI-allowed posts changed since a time by a registered filter's
  verdict and the tags' verdict for given pairs, with invalid answers, cost per tagged post and p95 latency; no post
  text and no write (`engineering/development/post-tags.md` § Shadow report).
