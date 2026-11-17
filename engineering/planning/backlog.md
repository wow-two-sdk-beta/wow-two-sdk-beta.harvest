# SCFD — Backlog

*Last updated: 2026-11-17*

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

## Events integration

Picks for `system/sessions/events-integration` in wow-two-ws (P1 = this goal, P2 = after it).

| Item | Type | Notes |
|---|---|---|
| Remaining `wow2.event.v1` fields | feature | events-integration #18, P2: certainty, time zone, occurrences, typed deadlines, participation, contact ownership, structured place and country, offer kinds, image `FileRef`, series; `kind` rides on `classification` |
| Issuer per environment | docs | events-integration #17, P1: dev trusts Ocharo's Identity (#4); a separate SCFD issuer comes later |
| Operating controls past v0.1 | feature | events-integration #33, P2: request allowance reserved in transport, retention purge that emits the reserved `post.purged`, freshness and consumer-lag readout, resync control, backup/restore test |
| Post card place line | feature | venue and city on a post card; needs the posts list read to carry the place, which the AI integration's extraction is to supply (console live edits, 2026-11-13) |
| Console iteration 2 | feature | events-integration #32, #33, #37, #45: the client's sign-in with Sources and Following; Home's questions (a page-limit partial counts as failed today); the Subscribers page's design; a source's AI card and outgoing-server line once they exist |
| Account name in the bar | engineering | fe sdk `AppFrame` 0.0.45: `AccountMenu`'s trigger shows initials only, with no prop for the name beside them; the fe sdk lane owns it |
| Capture view and branch sources | feature | a post's "See the capture" needs a route that shows a stored capture; a store's branch needs a `branch` source family, which the console's icon map already names |
| Granting a restricted source | feature | a source registered `restricted` keeps its sections pending approval; neither the console nor the API can grant it, so an operator grant (permission and its pipelines' state) is needed before sources come from outside the adapter registry |
| Classification writer | feature | events-integration #55, P2: the model tier fills `PostClassification` (`classifier`, `verdict`, `confidence`, `reason`, `kind`); the shape ships in v0.1 iteration 14, no step writes it yet |
| Console on a target | engineering | events-integration #56, P2: `deploy.yml` routes only `/api`; serving the console needs `/`, `Harvest:Console:*`, `Identity:Client:*`, `DataProtection:KeysPath` with a keys volume and an Identity console client; follows the layout redesign |
| First NuGet and npm publish | engineering | events-integration #15, P2: Events keeps its own wire reader (#43), so no consumer waits; needs `PUBLISH_PACKAGES` and the nuget.org trusted-publishing policy |
| Afisha intake and terms | policy | events-integration #32, #50: public category and article pages inside `robots.txt`; captures stay internal review evidence; nothing is republished; facts plus an attributed link inside Ocharo |
| Afisha deadline and participation readings | feature | events-integration #18, P2: typed application deadlines and participation routes instead of `schemaOrg.afisha` evidence |

## Features

| Feature | State | Spec |
|---|---|---|
| Pipelines, steps and runs | implemented v0.1; acceptance open | — |
| Posts, dedup and deletion checks | implemented v0.1; acceptance open | — |
| Pull delivery | implemented v0.1; acceptance open | — |
| Console | implemented v0.1; acceptance open | — |
| Webhook delivery | planned | — |
| Link intake and manual entries | planned | — |
| Tag and keyword subscriptions | planned | — |
| AI extraction tiers | planned | — |

## Specced next

ChatGPT builds these in the pull order of wow-two-ws `system/sessions/events-integration/tasks/tasks.md`; each row's spec is its id in the named file there. Delete a row when it ships, with any older row it covers.

| Item | Type | Notes |
|---|---|---|
| S07 Commit the work in slices and verify v0.2.7 on dev | engineering | spec in `tasks-scfd.md` |
| S36 Leftovers: the console reads `processorLabels`; a stage-editor gate test | fix | spec in `tasks-scfd.md`; backend shipped, see v0.2 Iteration 4 |
| S39 Leftovers: Settings says "Harvested by stages"; drop `RequiresGrant` / `AwaitingSourceAccess` | engineering | spec in `tasks-scfd.md`; left to the Codex OLX/AI work, which holds those files |
| S38 Decide what the marketplace collectors keep about people | decision | spec in `tasks-scfd.md` · needs the developer |
| S18 Run ANONS, Craftfair, Uzexpocentre and CAEx for Ocharo | product | spec in `tasks-scfd.md` |
| S09 Let one subscription follow many sections | feature | spec in `tasks-scfd.md`; Instagram needs one subscription across publisher pipelines. Collection pipelines stay independent of subscriber interests. Apply the same declared scope to stored-post replay and future delivery; a shared Instagram source is intentional, not a requirement for separate publisher subscriptions. |
| E02 Tighten ocharo-fashion-v1 against off-topic event matches | fix | spec in `tasks-events-path.md` |
| E19 Let a subscriber list its tenant's subscriptions | feature | spec in `tasks-events-path.md` |
| S10 Ocharo filter regression set over every reader's fixtures | engineering | spec in `tasks-scfd.md` |
| S19 Read trade-fair edition pages (TITF, Texworld, TFT Expo) | feature | spec in `tasks-scfd.md` |
| S20 Keep one size of each Afisha picture | fix | spec in `tasks-scfd.md` |
| S11 Emit item-level publisher categories as source tags | feature | spec in `tasks-scfd.md` |
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
| A05 SCFD relevance pass in shadow on one Afisha pipeline | feature | spec in `tasks-platform.md` · owner: ev-ai lane |
| S14 Embed the shared AI layer in the source host | engineering | spec in `tasks-scfd.md` |
| S15 Run Ocharo relevance in shadow on Afisha | feature | spec in `tasks-scfd.md` |
| S16 Promote AI relevance into Ocharo's delivery | product | spec in `tasks-scfd.md`; connect completed AI results to consumer delivery before evaluating requested event filters. Capturing Instagram posts and storing separate processing results do not establish this path. Verify accepted, rejected and unresolved results end to end before promotion. |
| S17 Estimate event dates from the AI pass | feature | spec in `tasks-scfd.md` |
| S33 Serve the console on harvest-dev | engineering | spec in `tasks-scfd.md` |
| S35 Publish the Harvest NuGet and npm family | engineering | spec in `tasks-scfd.md` |
| S34 Drop IP-lease pools and CAPTCHA solving from the image | engineering | spec in `tasks-scfd.md` |

## Sources

| Item | Type | Notes |
|---|---|---|
| Instagram Phase 1 target acceptance | product | the collector ships in v0.2.12 and local capture/deduplication passed. After the shared Ocharo AI/delivery work closes, configure the test connection on `harvest-dev`, register approved publisher profiles, enable chosen table-managed schedules and verify scheduled capture through filtered delivery to Events/Desk. Production requires a separate account and grant. See [Instagram operation and readiness](../deployment/instagram.md#integration-boundary-and-phase-1-acceptance). |
| De-duplicate a post's images | feature | a post lists one picture at two sizes (Afisha: `…_l.webp` from `og:image`, `…_m.webp` from the page), so the console's carousel steps between near-identical images; needs a rule that keeps the largest size per picture in the reader, a generic de-dup by `PostMedia.Hash` once images are fetched, the posts list counting distinct pictures, and a `refresh` run for stored posts (console live edits, 2026-11-13) |
| Telegram `t.me/s` reader | feature | a reader in `sources/` |
| Live event-source coverage | feature | complete bounded acceptance of ANONS, Craftfair and Uzexpocentre, and review dates and access evidence, as Afisha passed on dev |
| Ocharo Identity on the source host | feature | operator email sign-in passes on the local console; the phone/code round trip, role review, sign-out and restart checks remain |
| Configured providers | feature | supply approved credentials and origins, then verify one real source protocol; a dedicated outgoing server per source is allowed, rotating proxy pools are not built (events-integration #50) |
| Render companion | engineering | core companion package on Playwright; iticket waits on it |
| ICS recurrence expansion and oEmbed | feature | recurrence masters and OpenGraph metadata work; expansion and secondary fetches remain separate |
| Broader date and language rules | feature | current text dates cover ISO and DD.MM.YYYY; unresolved text stays unresolved |

## Engine

| Item | Type | Notes |
|---|---|---|
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
| Security pin on SignalR Redis | engineering | `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.15 is pinned transitively (GHSA-wr8v-9x5q-ggcm on 10.0.3, from be sdk `10.0.111-beta`); drop the pin once the be sdk ships the patched version |
