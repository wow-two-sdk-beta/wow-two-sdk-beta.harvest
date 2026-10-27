# wow-two-sdk-beta.harvest — Backlog

*Last updated: 2026-10-27*

Every unbuilt item, grouped; top of each group = next. Shipped work lives in [`version-track/`](version-track/).
Shape: wow-two-ws/conventions/planning/version-track/version-track.md

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

## Delivery

| Item | Type | Notes |
|---|---|---|
| Webhook push | feature | waits on the core durable dispatcher |
| Retention purge | feature | a stub in v0.1 |

## Release

| Item | Type | Notes |
|---|---|---|
| GitHub repo, settings and `main-guard` ruleset | engineering | auto mode refused `gh repo create`; the developer creates it |
| Core pin past `10.0.81-beta` | engineering | core `10.0.82` moved `Identity/ApiKeys` to the Identity repo; re-pin with `WoW2.Sdk.Beta.Be.Identity.Auth` (same namespace) once it publishes; `core-latest.yml` is red until then |
| Image and Wheelhouse dev rollout | engineering | after the GitHub repo |
| NuGet trusted-publishing policy and npm first version | engineering | after the content gate |
| Move the Harvest analysis into `engineering/research/` | engineering | after the private-facts strip pass |
