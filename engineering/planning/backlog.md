# wow-two-sdk-beta.harvest — Backlog

*Last updated: 2026-10-27*

Every unbuilt item, grouped; top of each group = next. Shipped work lives in [`version-track/`](version-track/).
Shape: wow-two-ws/conventions/planning/version-track/version-track.md

## Features

| Feature | State | Spec |
|---|---|---|
| Pipelines, steps and runs | planned | — |
| Capability ladder | planned | — |
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
| Proxy and IP-server egress | feature | core `Http/Egress`; built inline first, extracted at the +0.1 |
| Captcha solver clients | feature | core module beside egress |
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
| Image and Wheelhouse dev rollout | engineering | after the GitHub repo |
| NuGet trusted-publishing policy and npm first version | engineering | after the content gate |
| Move the Harvest analysis into `engineering/research/` | engineering | after the private-facts strip pass |
