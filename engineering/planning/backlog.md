# SCFD — Backlog

*Last updated: 2026-11-21*

Every unbuilt item, grouped; top of each group = next. Shipped work lives in [`version-track/`](version-track/).
Shape: wow-two-ws/conventions/planning/version-track/version-track.md

## Console

| Item | Type | Notes |
|---|---|---|
| Instagram Capture stage controls | feature | Phase 1: expose the adapter's supported Capture operation and cadence in the generic pipeline overview. Current stage cards cover only Discover/Harvest/Process; Instagram has neither a discovery catalog nor a Detail step, so those first two cards cannot harvest it. Source Settings already offers working Capture now. The engine accepts `kind=capture` schedules through pipeline updates; the generic stage-settings API/UI must expose that supported path without inventing a catalog or treating Process as collection. |
| Post card composition | engineering | review date layout first, then isolate the card from list state and adopt the SDK primitives that preserve card links and controls |
| Adaptive pagination in the frontend SDK | engineering | extract the cursor loader and scroll-velocity prefetch after reviewing both SCFD views. Reuse the SDK virtual windowing, test slow networks and keyboard navigation, and expose page-size bounds to consumers. |
| Afisha publisher category coverage | feature | review categories beyond Fashion, Exhibitions and Concerts against saved publisher evidence. Keep publisher category, event timing and harvest section available as facets. |
| Wire Files image compaction | engineering | request a Files compaction preset when mirroring post images. Consume durable Files variant-ready events idempotently and associate variant references with the current original. Serve variants through the authorized media route with original bytes as the fallback. |
| Listing Posts filters | feature | extend the OLX room-count pilot with rent or sale, property type, district and price. Price comparisons use a known currency and billing period. The listing integration lane owns those additions. |
| Extract shared pipeline tooling to the SDKs | engineering | validate the stage and run contracts with SCFD and Haven. Move common scheduling, queue and audit contracts into the backend SDK. Move the stage flow, live run monitor and history into the frontend SDK. Keep adapter plans and settings in the apps. |
| Extract the photo gallery to the frontend SDK | engineering | move the preview grid and overflow tile alongside the shared lightbox after reviewing SCFD and Haven together. |
| Adopt the fe `0.0.51` lifts in the console | engineering | `MediaCard`, `Button` `isUnavailable`, `MetaInlineItem`, `Grid` `minColumnWidth` and `Badge` `isShrinkable` (events-integration F05-F10) are pinned with 0 uses |
| Full Activity history | feature | Activity shows the latest 50 runs per pipeline |
| Browsing state in the URL | feature | search, filters, collapse and scroll live in the console session only, so a link or reload loses them |
| Custom source-group glyph | feature | the shared Group glyph is provisional |
| Pipelines in the sidebar or the operator menu | decision | the console lists Pipelines in the sidebar; console decision #42 puts it in the operator menu · needs the developer |
| Map pins for processed places | feature | a processed event's or listing's place opens as a Yandex Maps pin and a Google Maps pin; a place neither search resolves is flagged on the card and queued for the custom locations list; resolution comes from the shared WoW2 Locations domain (wow-two-ws `docs/platform-apps.md` #18), never a console-only geocoder · the links shipped 2026-11-20 as the fe sdk `domain/maps` `MapLinks` (a search on the place text, no geocoding); the links now show in the console beside every place; the unresolved flag and the custom list wait for Locations |
| Evidence for every extracted field | feature | the processed post page quotes evidence only for dates, prices, registration and a listing's price and terms; the extraction answer stores none for name, place, format, access and status, so their rows show no quote. Add evidence spans to those fields in both extraction prompts, then the page shows them |
| Console cards in light theme and at phone width | engineering | the 2026-11-20 card round (content-anchored blur, md pills, ↗ on Processed cards, the provider pill, the processed post page) was checked in dark theme at desktop width, and the page also at 390 px over the demo client; light theme and the dev host remain |
| Available page lead | fix | the page shows its route description and the panel's own lead line, two lines saying the same; keep one |

## Events integration

Picks for `system/sessions/events-integration` in wow-two-ws (P1 = this goal, P2 = after it).

| Item | Type | Notes |
|---|---|---|
| Deliver a late image copy's file id | feature | the source-asset worker copies a post's images after the post is stored, and `wow2.event.v1` fills `media[].fileRef` only when a page is pulled, so an event pulled before its copy carries `fileRef: null` until the post's next event; emit a delivery update when a copy completes, so subscribers get the id without waiting |
| Remaining `wow2.event.v1` fields | feature | events-integration #18, P2: certainty, time zone, occurrences, participation, contact ownership, structured place and country, offer kinds, series; `kind` rides on `classification` |
| Issuer per environment | docs | events-integration #17, P1: dev trusts Ocharo's Identity (#4); a separate SCFD issuer comes later |
| Operating controls past v0.1 | feature | events-integration #33, P2: request allowance reserved in transport, retention purge that emits the reserved `post.purged`, freshness and consumer-lag readout, resync control, backup/restore test |
| Console iteration 2 | feature | events-integration #32, #33, #37, #45: the client's sign-in with Sources and Following; Home's questions (a page-limit partial counts as failed today); the Subscribers page's design; a source's AI card and outgoing-server line once they exist |
| Account name in the bar | engineering | fe sdk `AppFrame` 0.0.45: `AccountMenu`'s trigger shows initials only, with no prop for the name beside them; the fe sdk lane owns it |
| Capture view and branch sources | feature | a post's "See the capture" needs a route that shows a stored capture; a store's branch needs a `branch` source family, which the console's icon map already names |
| First NuGet and npm publish | engineering | events-integration #15, P2: Events keeps its own wire reader (#43), so no consumer waits; needs `PUBLISH_PACKAGES` and the nuget.org trusted-publishing policy |
| Afisha intake | policy | events-integration #32, #50: public category and article pages inside `robots.txt`, paced; internal use only; each item credits Afisha by its external link |
| Afisha participation readings | feature | events-integration #18, P2: participation routes instead of `schemaOrg.afisha` evidence |

## Features

| Feature | State | Spec |
|---|---|---|
| Pipelines, steps and runs | shipped v0.1 | — |
| Posts, dedup and deletion checks | shipped v0.1 | — |
| Pull delivery | shipped v0.1 | — |
| Console | shipped v0.1 | — |
| Webhook delivery | planned | — |
| Link intake and manual entries | planned | — |
| AI extraction tiers | partial v0.2: Layer 1 `post-tags` (iteration 10) and Layer 2 `extract-event` · `extract-listing` (iteration 11) | `development/post-tags.md` · `development/extraction.md` |
| Entity layer: entity types over the collector | partial v0.2: events and listings resolved from extraction answers (iteration 12); entity reads and the entity change feed (iteration 13); the `fe-harvest` mirror and the console's Entities page (2026-11-19); per-subscription routing of entity events (S09) remains | `development/entities.md` · wow-two-ws `ideas/scfd-entity-model.md` |

## Specced next

ChatGPT builds these in the pull order of wow-two-ws `system/sessions/events-integration/tasks/tasks.md`; each row's spec is its id in the named file there. Delete a row when it ships, with any older row it covers.

| Item | Type | Notes |
|---|---|---|
| S07 Commit the work in slices and verify v0.2.7 on dev | engineering | spec in `tasks-scfd.md`; the commits are accepted, and its dev acceptance runs as v0.2 Verification checks |
| S38 Decide what the marketplace collectors keep about people | decision | spec in `tasks-scfd.md` · needs the developer |
| Craftfair challenge-detector posture | decision | `GenericChallengeDetector` reads Craftfair's complete 200 listing as `javascript_interstitial` because Cloudflare's passive JS-detection script is on the page; the detector changes only on the developer's posture call (#50), before S18 runs Craftfair · needs the developer |
| S18 Run Craftfair for Ocharo after its challenge posture call | product | spec in `tasks-scfd.md`; ANONS, Uzexpocentre and CAEx run on dev on schedule (v0.2) |
| E19 Let a subscriber list its tenant's subscriptions | feature | spec in `tasks-events-path.md` |
| S19 Read trade-fair edition pages (TITF, Texworld, TFT Expo) | feature | spec in `tasks-scfd.md` |
| S20 Keep one size of each Afisha picture | fix | spec in `tasks-scfd.md`; a generic de-dup by `PostMedia.Hash` and a posts list that counts distinct pictures go with it |
| S12 Record each subscription's pull lag | feature | spec in `tasks-scfd.md` |
| S21 Route registry channels to the generic readers | feature | spec in `tasks-scfd.md` |
| S22 Read Hunarmand association calls | feature | spec in `tasks-scfd.md` |
| S23 Read regional tourism news on gov.uz | feature | spec in `tasks-scfd.md` |
| S24 Read public Telegram channels through t.me/s | feature | spec in `tasks-scfd.md` |
| S26 Read iTicket and ShowGo event listings | feature | spec in `tasks-scfd.md` |
| S27 Stop counting page-limit harvests as failures | fix | spec in `tasks-scfd.md` |
| S28 Let adapters declare each section's topic | engineering | spec in `tasks-scfd.md` |
| S29 Show an event's place on cards and the post page | feature | spec in `tasks-scfd.md` |
| S30 Event timing and an event-date sort for every source | feature | spec in `tasks-scfd.md` |
| S31 Give Ocharo a client view with Sources and Following | feature | spec in `tasks-scfd.md` |
| S32 Let an operator grant a restricted source | feature | spec in `tasks-scfd.md` |
| S35 Publish the Harvest NuGet and npm family | engineering | spec in `tasks-scfd.md` |
| S34 Drop IP-lease pools and CAPTCHA solving from the image | engineering | spec in `tasks-scfd.md` |

## Sources

| Item | Type | Notes |
|---|---|---|
| ANONS JavaScript challenge | engineering | 2026-11-20 08:28 UTC, at 6 pages and a 30 s pace, 1 of 6 requests read as `javascript_interstitial` (`fetch_outcome` challenge); the lane paused 15 min as designed and 2 new posts were captured. First check whether it is a real challenge or the detector's false positive (as with Craftfair's complete 200 listing), then measure a week's rate and lower pages or pace if it repeats; never solve or bypass a challenge |
| Instagram Phase 1 target acceptance | product | the collector ships in v0.2.12 and local capture/deduplication passed. After the shared Ocharo AI/delivery work closes, configure the test connection on `harvest-dev`, register approved publisher profiles, enable chosen table-managed schedules and verify scheduled capture through filtered delivery to Events/Desk. Production requires a separate account and grant. See [Instagram operation and readiness](../deployment/instagram.md#integration-boundary-and-phase-1-acceptance). |
| Ocharo Identity on the source host | feature | operator email sign-in passes on the local console; the phone/code round trip, role review, sign-out and restart checks remain |
| OLX phone reveal | decision | not built (#17, 2026-11-21). OLX reveals a phone through its friction gate (`friction.olxgroup.com` challenge → token → `limited-phones`), which escalates `delay` → `recaptcha_v2_checkbox` → `blocked` after about 20 reveals per window (Haven history). Revealing in volume would mean solving reCAPTCHA v2 to defeat a control against bulk phone harvesting, and the building agent declines that; auto mode refused even the read-only probe. The `TwoCaptchaSolver` stays uncalled. Route chosen by the developer, 2026-11-21: only phones the seller published (display name, title, text, attributes, `tel:` links) go into `contacts` as E.164 plain text through `e164-and-email@3`; a count on 180 stored OLX posts found them in 29 (27 in the display name). Delivering contacts to Haven is a separate call |
| Configured providers | feature | supply approved credentials and origins, then verify one real source protocol; a dedicated outgoing server per source is allowed, rotating proxy pools are not built (events-integration #50) |
| Render companion | engineering | core companion package on Playwright; iticket waits on it |
| ICS recurrence expansion and oEmbed | feature | recurrence masters and OpenGraph metadata work; expansion and secondary fetches remain separate |
| Broader date and language rules | feature | current text dates cover ISO and DD.MM.YYYY; unresolved text stays unresolved |

## Engine

| Item | Type | Notes |
|---|---|---|
| Entity-type architecture | feature | a basic collector (sources, pipelines, posts) with entity-type modules on top, each with its own logic: events link recurring editions, marketplace dedups and researches prices, news gets its own components; other source wrappers may feed it too (developer, 2026-11-18: "we need entity type based architecture and we'll break it down as such later"). Deadline 2026-11-30, wow-two-ws `pln-tasks.md` `app-t-010`. Build plan in wow-two-ws `ideas/scfd-entity-model.md`: step A shipped in v0.2 iteration 10 — the Layer 1 `post-tags` pass, the IAB 3.1 taxonomy, tags written back as revisions, unsure flags and the `harvest:post:` eval subject (`development/post-tags.md`); step B shipped in iteration 11 — Layer 2 `extract-event` and `extract-listing`, gated by
wanted (entity type, domain) pairs until the selector supplies demand, evidence-checked into `harvest_post_extractions`
(`development/extraction.md`); step C shipped in iteration 12 — the entity layer: events with occurrences, cadence and a
predicted next run, listings with offers across authors and sites, role-bound relations, unsure flags, merge and split
(`development/entities.md`); step D shipped in iteration 13 — `IEntitiesBroker` (search with years, an entity's timeline,
its posts per year from every source, the calendar) and the entity change feed `harvest_entity_events` under
`api/entities`; step E (evals) remains |
| Event entities after AI classification | feature | one record per event as the single source of truth: posts and articles bind to it, past editions attach to it, cadence (annual, quarterly, monthly) and the next edition are predicted; subscribers search events as history, by year, with every source's posts for that year, beside the post feed (developer, 2026-11-18: "one entity / one source of truth for events"). First entity type of the architecture row above. Records, editions, cadence and prediction shipped in v0.2 iteration 12 with the operator's `entities search` and `show`; subscriber reads (search with years, posts per year, the calendar) and the entity change feed shipped in iteration 13 (build plan step D); a subscription choosing posts, entities or both waits for S09 |
| Entity change feed retention | engineering | `harvest_entity_events` keeps every row; decide a retention window with the subscription log's and prune old rows, keeping cursors within retention replayable |
| Split AI processing per entity type | engineering | after the AI layer is reliable and refined to what Ocharo needs: close this lane, write a prompt for a separate chat, and that chat moves AI processing out as separate tasks, or into separate SCFD versions per entity type (events, listings, news) (developer, 2026-11-19: "once we are make ai integration reliable, refine as much as ocharo needs - we'll close it, prep a prompt for a separate chat, and it will work on extracting ai processing as separate tasks or just completely separate versions of scfd - one for events, one for listings, one for news etc"). Gate: Ocharo's shadow week read and its subscription selector serving; starts from `development/ai-operations.md` and the entity-type architecture row |
| AI evals and stats console pages | feature | console pages for eval sets, runs and reports (`api/ai/evals/*`), and for AI stats: answers by status, entity type and domain, flags, cost and latency over time, per use case and model, from `post-tags report`, `extract report` and the inference log. Built by a separate SCFD orchestrator, not under the Ocharo one (developer, 2026-11-19: "we'll need evals and stats pages in scfd next - since we are working under ocharo orchestrator chat, that will be build with separate scfd orchestrator"). Starts from `development/ai-operations.md` and v0.2 iteration 20's console pages |
| Native structured output for the extraction schemas | engineering | `extract-event` and `extract-listing` schemas exceed Anthropic's structured-output grammar ("compiled grammar is too large"), so AI `10.0.4-beta`+ sends them in the instructions and harvest repairs out-of-list values (`AnswerNormalizer`, `CheckEnums` false). It works, at a few extra tokens per call and with no provider-enforced enums. Fix: fewer nullable union fields or a split schema, until the provider takes it natively; then measure answer validity against the fallback (developer, 2026-11-19: "add this to the backlog, we'll try to fix later"). Not blocking Ocharo |
| Resumable historical backfill for feed sources | feature | deferred; shared policy for Instagram and later Telegram, persisted per feed in tables. The admin explicitly enables backfill and chooses the oldest post publication date; initialize it to three calendar months before activation and persist that fixed cutoff. No hardcoded two-year or other history-duration ceiling. Save captured posts and continuation checkpoints durably across runs/restarts; batch budgets preserve unfinished progress. A separate service prioritizes feeds and dispatches small background batches under shared quotas, backoff and pause controls, independently of recent collection. Report cutoff reached, provider history exhausted and interrupted/incomplete coverage distinctly. AI owns event-date extraction and recurrence. |
| Adaptive scheduling and batch sizing | feature | tune each stage from content volume, queue age, yield and refusals. Respect operator bounds and shared host budgets. Store each decision in the run audit. Current adaptive schedules use their minimum interval. |
| Full-sweep absence inference | feature | current deletion evidence comes from direct known-link checks |
| Per-request budget enforcement within a step | engineering | current request/byte budgets stop at step boundaries, including robots and redirect usage |
| Consumer JSON Schema registration and validation | feature | deterministic event projection is available; arbitrary schema validation remains separate |
| Versioned source schemas and immutable readings | feature | native `extras.source` snapshots are inspectable; schema enforcement and mapping history remain separate |
| Availability attempt history and forced per-link checks | feature | link observations are exposed; failed attempts and source-aware HTTP 200/soft-deletion checks remain separate |
| Contact ownership and participation evidence | feature | normalized phones/emails retain raw text; owner, purpose and evidence spans need a richer contract |

## Delivery

| Item | Type | Notes |
|---|---|---|
| Webhook push | feature | waits on the core durable dispatcher |

## Release

| Item | Type | Notes |
|---|---|---|
| Move the Harvest analysis into `engineering/research/` | engineering | after a pass that strips personal and host facts |

## Brand

| Item | Type | Notes |
|---|---|---|
| Redesign the SCFD logo | product | Deferred. The user rejected the draft on 2026-11-09. Review new concepts before replacing the logo and its icon exports. |

## One product

Decisions of the merge (v0.2 iteration 1) and what they leave.

| Item | Type | Notes |
|---|---|---|
| Names of the source projects | engineering | the readers keep `WoW.Two.Sdk.Beta.Be.HarvestAdapters.*` and the host `HarvestSources.Api`, so the merge renamed nothing; one rename to `Harvest.Sources.*`, with the host and Hosting's `Harvest.Api.*` namespaces, is a later, mechanical pass |
| One test project per tier | engineering | `HarvestAdapters.Tests.{Unit,Integration}` and `HarvestSources.Tests.E2E` sit beside `Harvest.Tests.*`; folding them into one project per tier needs no behaviour change |
| `HarvestSources:Audience` | question | `public` existed for an image without the readers; one image now always carries them, so the switch only hides the Ocharo routes; keep or drop is a behaviour question for the developer |
| Drop the SignalR Redis security pin | engineering | the transitive `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.15 pin (GHSA-wr8v-9x5q-ggcm) is redundant: the pinned be sdk `10.0.113-beta` brings 10.0.15 itself |
