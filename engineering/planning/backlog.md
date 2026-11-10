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
| Console layout iteration 2 | feature | events-integration #56, #58: the developer's review of iteration 1; an existing pipeline's URL is still editable by hand; the Sources card repeats the Fashion and Exhibitions section chips; Home's delivery lag, a container-wide runs list and API-key listing need API reads that do not exist yet |
| fe sdk `0.0.45` pin and `AppFrame` | engineering | events-integration #59, after the developer's review of iteration 1, not before: the console adopts the shared `AppFrame` (56 px bar, 248 px sidebar, 64 px rail as CSS variables) from the fe sdk's `engineering/architecture/app-frame-migration.md`, console section; breaking on the pin: `theme-platform` carries `--glass-*` tokens, so the bar and cards take the glass look, and `BffOrganization` gains `name` and `slug` (`string \| null`), which a hand-built `BffUser` in a fake or demo session must add |
| Classification writer | feature | events-integration #55, P2: the model tier fills `PostClassification` (`classifier`, `verdict`, `confidence`, `reason`, `kind`); the shape ships in v0.1 iteration 14, no step writes it yet |
| Console on a target | engineering | events-integration #56, P2: `deploy.yml` routes only `/api`; serving the console needs `/`, `Harvest:Console:*`, `Identity:Client:*`, `DataProtection:KeysPath` with a keys volume and an Identity console client; follows the layout redesign |
| First NuGet and npm publish | engineering | events-integration #15, P2: Events keeps its own wire reader (#43), so no consumer waits; needs `PUBLISH_PACKAGES` and the nuget.org trusted-publishing policy |

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
| Private adapters repo with OLX, Domtut and Afisha brokers | engineering | one package per source on a private feed; it builds WoW2's Harvest image |
| Telegram `t.me/s` broker | feature | private adapters repo |
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
| Image and Wheelhouse dev rollout | engineering | dev runs `harvestadapters` alone (events-integration #45); public `harvest` stays `deploy: none` |
| Move the Harvest analysis into `engineering/research/` | engineering | after the private-facts strip pass |

## Brand

| Item | Type | Notes |
|---|---|---|
| Redesign the SCFD logo | product | Deferred. The user rejected the draft on 2026-11-09. Review new concepts before replacing the logo and its icon exports. |
