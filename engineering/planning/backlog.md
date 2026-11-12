# SCFD — Backlog

*Last updated: 2026-11-10*

Every unbuilt item, grouped; top of each group = next. Shipped work lives in [`version-track/`](version-track/).
Shape: wow-two-ws/conventions/planning/version-track/version-track.md

## Events integration

Picks for `system/sessions/events-integration` in wow-two-ws (P1 = this goal, P2 = after it).

| Item | Type | Notes |
|---|---|---|
| Remaining `wow2.event.v1` fields | feature | events-integration #18, P2: certainty, time zone, occurrences, typed deadlines, participation, contact ownership, structured place and country, offer kinds, image `FileRef`, series; `kind` rides on `classification` |
| Issuer per environment | docs | events-integration #17, P1: dev trusts Ocharo's Identity (#4); a separate SCFD issuer comes later |
| Operating controls past v0.1 | feature | events-integration #33, P2: request allowance reserved in transport, retention purge that emits the reserved `post.purged`, freshness and consumer-lag readout, resync control, backup/restore test |
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

## Sources

| Item | Type | Notes |
|---|---|---|
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
| Adaptive cadence tuning and idle lifecycle | feature | current adaptive schedules use their minimum interval |
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
| Names of the source projects | engineering | the readers keep `WoW.Two.Sdk.Beta.Be.HarvestAdapters.*` and the host `HarvestSources.Api`, so the merge renamed nothing; one rename to `Harvest.Sources.*` / `Harvest.Api` is a later, mechanical pass |
| One test project per tier | engineering | `HarvestAdapters.Tests.{Unit,Integration}` and `HarvestSources.Tests.E2E` sit beside `Harvest.Tests.*`; folding them into one project per tier needs no behaviour change |
| The generic host `Harvest.Api` | engineering | the image ships `HarvestSources.Api`; `Harvest.Api` stays as the host the E2E and conformance tiers run, with no readers registered; retire it once those tiers run against the source host |
| `HarvestSources:Audience` | question | `public` existed for an image without the readers; one image now always carries them, so the switch only hides the Ocharo routes; keep or drop is a behaviour question for the developer |
| Security pin on SignalR Redis | engineering | `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.15 is pinned transitively (GHSA-wr8v-9x5q-ggcm on 10.0.3, from be sdk `10.0.111-beta`); drop the pin once the be sdk ships the patched version |
