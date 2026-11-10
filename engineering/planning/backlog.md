# SCFD — Backlog

*Last updated: 2026-11-10*

Every unbuilt item, grouped; top of each group = next. Shipped work lives in [`version-track/`](version-track/).
Shape: wow-two-ws/conventions/planning/version-track/version-track.md

## Events integration

Picks for `system/sessions/events-integration` in wow-two-ws (P1 = this goal, P2 = after it).

| Item | Type | Notes |
|---|---|---|
| Identity scope names `harvest.{noun}-{verb}` | engineering | events-integration #16, P1: resource `harvest`; 11 scopes, two segments, mapped to the registry's colon names in `CallerResolver`; docs sample fixed |
| Client credentials in the Client package | engineering | events-integration #15, P1: `AddHarvestDataClient` takes the be sdk's `OAuth2ClientCredentials` beside API keys, as the FSS Client does |
| Event evidence: mapping stamp, capture, pipeline | feature | events-integration #19, P1: `wow2.event.v1` gains `mapping` `{schema, norm, rules, parser}` and a top-level `capture`; `DeliveryEvent` gains `PipelineId`; content hash and capture file P2 |
| Organizer only from a declared organizer; `postedAt` | fix | events-integration #18, P1: the author or publisher no longer stands in for the organizer |
| Remaining `wow2.event.v1` fields | feature | events-integration #18, P2: certainty, time zone, occurrences, typed deadlines, participation, contact ownership, structured place and country, offer kinds, image `FileRef`, kind, series |
| Issuer per environment | docs | events-integration #17, P1: dev trusts Ocharo's Identity (#4); a separate SCFD issuer comes later |
| Operating controls past v0.1 | feature | events-integration #33, P2: request allowance reserved in transport, retention purge, freshness and consumer-lag readout, resync control, backup/restore test |
| First NuGet and npm publish | engineering | events-integration #15, P1: Events references `Harvest.Abstractions`, `.Client`, `.Testing`; needs `PUBLISH_PACKAGES` and the nuget.org trusted-publishing policy |

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
