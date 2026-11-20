# PostExtraction/

Layer 2 extraction (wow-two-ws `ideas/scfd-entity-model.md` build plan step B): a post whose done `post-tags` answer
matches a wanted (entity type, domain) pair gets its fields parsed into one canonical shape per entity type, with a
verbatim quote behind every date, price and rental term. The entity layer (step C) reads the stored answers; the post
takes only an `extract-event` answer's format, as its classification's kind, and an estimated date.

| Implements | Over |
|---|---|
| `IPostExtractionBroker` (run, backfill, report, the worker's tick) · `IPostExtractionDemand` (the demand gate: the configured list plus each active selector subscription's (entity type × domain) pairs) | the shared AI contract `WoW2.Sdk.Beta.Be.Ai.Abstractions` (`IAiRunner`, `IPromptsBroker`, `IInferencesBroker`); the Tagging lane's done answers, subject read and content hash; the Posts lane's queue hook |

- Owns: `harvest_post_extractions` and the `extract-event` / `extract-listing` kinds of `harvest_post_work` (013).
- Demand: `Harvest:Processing:Extraction:Wanted` lists `{ entityType, domain }` pairs (`event` + `style-fashion`,
  `listing` + `real-estate`). A post qualifies when its done tags have the pair's entity type and any node at or under
  the key (`style-fashion/womens-fashion` is under `style-fashion`; `style-fashion-x` is not). A host that composes the
  selectors registers the Delivery lane's `SelectorPostExtractionDemand` in place of `ConfiguredPostExtractionDemand`: it
  adds each active selector's (entity type × domain) pairs, its required entity types or both, crossed with its domain
  includes, and the configured list keeps serving pairs no selector names. The selector, not this lane, decides what a
  subscription receives (`subscription-selectors.md`).
- Queue: the Posts lane queues in the write's transaction — after a done tags answer about the current content, and on
  any resolve whose content already has done tags — keyed by the same title-and-text hash, so the tag-only revision
  queues nothing and a changed text queues again once its new tags are done. `extract backfill` queues stored posts for
  pairs added later. Automatic queueing needs every source supplying the title and text in `auto` mode; an explicit row
  (named posts, listed sources, `run --post` tags) also serves `manual`. The worker re-checks the post, its hash, its tags, the mode and
  the demand before each call; a re-run's row (`run_id`) skips the demand and needs only tags of the use case's entity
  type. `QueueAsync(force: true)` queues over a done answer for a re-run, and a done answer passes its run to the
  entity queue.
- Call: use case `extract-event` or `extract-listing`, versioned, `KeepOutputs` true, the code-owned schema
  (`PostExtractionDefinitions`, the listing types in `ListingAnswer.cs`); variables `title`, `text` (cut to
  `MaxTextChars`), `sourceCategory`, `language`, `dates`, `place`, `prices`, `postedAt`, and the tags `entityType`,
  `domains`, `about`, `timeframe`, `titleEn`; `extract-listing` adds `attributes`, `section` and `seller`
  (`PostExtractionContent.VariablesFor`, `PostExtractionDefinitions.VariablesOf`): `attributes` (an OLX ad's attribute lines)
  and `seller` (its seller category) are OLX-only and empty for any other post, while `section` names the section's catalog
  key, else the pipeline's name, for any source (`PostSections`). Routes
  come from `Harvest:Processing:Extraction:Models`, default `anthropic/claude-haiku-5-5`; `PromptVersions` pins a
  version per use case.
- Scrub: `title` and `text` pass `../Tagging/PhoneScrub` first (a phone becomes `[phone]`), as do the attribute lines;
  the quote corpus is built from the scrubbed text, so a quote can never hold a number the prompt did not. The listing
  schema has no field for a name, phone or contact. `extract-listing` is at version 2 of its schema, replaced in place
  under the same use case key: the `extract prompt` verb refreshes a stored older code schema (the SQL checks of 013,
  014 and 017 name the key).
- Normalizing: `PostExtractionDefinitions.Parse` runs `AnswerNormalizer` before the strict read, so a model value the
  schema lacks no longer discards the answer. A value outside an enum becomes `unsure` (else `unknown`, else `null`
  for a nullable field) with a flag `{field: json path, reason: "value outside the list", proposed}`; a list drops the
  item and removes duplicates; case and `-` `_` space variants map without a flag; an unknown property is removed with
  a flag. A wrong JSON type, a missing field and a required enum with no fallback (`occurrences[].precision`) still
  reject the answer. The flags join the answer's `flags` and pass `PostExtractionValidation` like the model's.
  `post-tags` uses the same normalizer (`../Tagging/tagging.md`); `rental-terms@2` keeps its strict read.
- Evidence: a quote must appear verbatim, on whole tokens, in the title or the part of the text the model read
  (`RentalTermEvidence.Quoted`); a rental term may also quote an attribute line of an OLX ad (`ExtractionCorpus.TermSource`,
  one more source, never joined to the text); a price's quote must also state its amount. A failing date, price or registration date
  is dropped, and a failing rental term turns `unknown` (`RentalTermEvidence.CheckTerms`, and
  `RentalTermEvidence.CheckListingTerms` for the listing's own types, `commission` included); each gets a flag. The rest
  of the answer stays. `RentalTermEvidence` and the rental-term types are shared with `rental-terms@2` in
  `sources/Module/Marketplace/`, which keeps rejecting a whole answer.
- Field rules: an unsure pick always carries a flag; `fashion` stays only for a primary domain under `style-fashion`;
  `rentalTerms` only for a `rent` offer whose primary domain is under `real-estate`; changes only on an `update`, with
  `status` added when the relation names a new status; free text is clipped, and a phone, email, handle or messenger link
  is removed with a `contacts` flag; the listing author keeps its kind only; the listing's unit and building facts sit in
  `item` (an area above the total is dropped, a floor above the floors is flagged), and every closed list drops an
  unknown value with a flag.
- Outcomes: `done` stores the answer and clears the queue row; `failed` (refused, schema-invalid, provider errors, an
  answer of the wrong shape: a wrong JSON type or a missing field) and `unavailable` (no runner, use case or prompt; a
  provider without key) store the status, no answer, and retry after `RetryDelay` × attempts, capped at a day. A failure
  never replaces a done answer.
- Listing delivery: a done `extract-listing` answer about the current content also goes to the subscriptions that hold the
  post as `post.extracted` with `extras.listingAi` (`ListingAiExtras`: schema, prompt version, model, cost, revision, answer,
  flags), through the Posts lane's write; a failed or unavailable answer delivers nothing. The record carries the run's
  `CostMicroUsd` for that (zero for a cached answer); the table does not store it.
- Section-implied route: a post whose tags a section implied (`Tagging/tagging.md`) has its rule tags in the write, so the
  Posts lane queues this lane's use case in the same transaction, in `auto` mode, without a `post-tags` answer.
- Entities: a done answer queues the post's `entity-resolve` row in the same transaction while `Harvest:Entities:Enabled`
  is set; the Entities lane (`../Entities/entities.md`) reads the answer, never this lane.
- Write-back: the answer is stored through the Posts lane (`ModulePostBroker.WriteExtractionAsync`), which resolves the
  post again in the same transaction for a done `extract-event` answer about the current content. The resolve overlays
  `PostExtractionOverlay`: the first occurrence as an `estimated` start (and end) when the readers state none — a stated
  date always wins — and the format as the classification's kind. A changed post is a new revision with `post.changed`.
