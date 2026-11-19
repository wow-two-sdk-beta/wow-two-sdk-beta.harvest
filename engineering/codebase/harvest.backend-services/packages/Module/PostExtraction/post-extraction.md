# PostExtraction/

Layer 2 extraction (wow-two-ws `ideas/scfd-entity-model.md` build plan step B): a post whose done `post-tags` answer
matches a wanted (entity type, domain) pair gets its fields parsed into one canonical shape per entity type, with a
verbatim quote behind every date, price and rental term. The entity layer (step C) reads the stored answers; this lane
never writes the post.

| Implements | Over |
|---|---|
| `IPostExtractionBroker` (run, backfill, report, the worker's tick) · `IPostExtractionDemand` (the demand gate; the configured default stands in for the subscription selector) | the shared AI contract `WoW2.Sdk.Beta.Be.Ai.Abstractions` (`IAiRunner`, `IPromptsBroker`, `IInferencesBroker`); the Tagging lane's done answers, subject read and content hash; the Posts lane's queue hook |

- Owns: `harvest_post_extractions` and the `extract-event` / `extract-listing` kinds of `harvest_post_work` (013).
- Demand: `Harvest:Processing:Extraction:Wanted` lists `{ entityType, domain }` pairs (`event` + `style-fashion`,
  `listing` + `real-estate`). A post qualifies when its done tags have the pair's entity type and any node at or under
  the key (`style-fashion/womens-fashion` is under `style-fashion`; `style-fashion-x` is not). The selector replaces
  `ConfiguredPostExtractionDemand` by registering its own `IPostExtractionDemand` first.
- Queue: the Posts lane queues in the write's transaction — after a done tags answer about the current content, and on
  any resolve whose content already has done tags — keyed by the same title-and-text hash, so the tag-only revision
  queues nothing and a changed text queues again once its new tags are done. `extract backfill` queues stored posts for
  pairs added later. The worker re-checks the post, its hash, its tags and the demand before each call.
- Call: use case `extract-event` or `extract-listing`, versioned, `KeepOutputs` true, the code-owned schema
  (`PostExtractionDefinitions`); variables `title`, `text` (cut to `MaxTextChars`), `sourceCategory`, `language`,
  `dates`, `place`, `prices`, `postedAt`, and the tags `entityType`, `domains`, `about`, `timeframe`, `titleEn`. Routes
  come from `Harvest:Processing:Extraction:Models`, default `anthropic/claude-sonnet-5-5`; `PromptVersions` pins a
  version per use case.
- Evidence: a quote must appear verbatim, on whole tokens, in the title or the part of the text the model read
  (`RentalTermEvidence.Quoted`); a price's quote must also state its amount. A failing date, price or registration date
  is dropped, and a failing rental term turns `unknown` (`RentalTermEvidence.CheckTerms`); each gets a flag. The rest of
  the answer stays. `RentalTermEvidence` and the rental-term types are shared with `rental-terms@2` in
  `sources/Module/Marketplace/`, which keeps rejecting a whole answer.
- Field rules: an unsure pick always carries a flag; `fashion` stays only for a primary domain under `style-fashion`;
  `rentalTerms` only for a `rent` offer whose primary domain is under `real-estate`; changes only on an `update`, with
  `status` added when the relation names a new status; free text is clipped, and a phone, email, handle or messenger link
  is removed with a `contacts` flag; the listing author keeps its kind only.
- Outcomes: `done` stores the answer and clears the queue row; `failed` (refused, schema-invalid, provider errors, an
  answer outside the schema) and `unavailable` (no runner, use case or prompt; a provider without key) store the
  status, no answer, and retry after `RetryDelay` × attempts, capped at a day. A failure never replaces a done answer.
- Entities: a done answer queues the post's `entity-resolve` row in the same transaction while `Harvest:Entities:Enabled`
  is set; the Entities lane (`../Entities/entities.md`) reads the answer, never this lane.
