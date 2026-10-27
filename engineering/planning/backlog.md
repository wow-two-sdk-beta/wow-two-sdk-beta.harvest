# wow-two-sdk-beta.harvest — Backlog

*Last updated: 2026-10-27*

Every unbuilt item, grouped; top of each group = next. Shipped work lives in [`version-track/`](version-track/).
Shape: wow-two-ws/conventions/planning/version-track/version-track.md

## Features

| Feature | State | Spec |
|---|---|---|
| Pipelines, steps and runs | planned | — |
| Posts, dedup and deletion checks | planned | — |
| Pull delivery | planned | — |
| Console | planned | — |
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
