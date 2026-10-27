# Harvest — capability

*Last updated: 2026-10-27*

## Problem

Products need the same source collection, scheduling, capture history, duplicate resolution, deletion checks and
delivery. Harvest owns that state once; a consumer subscribes to a pipeline and applies its highest post revision.
Events is the first intended consumer. A consumer can select the deterministic `wow2.event.v1` projection rather
than own a second harvesting engine.

## Forms

| Form | Artifact | State |
|---|---|---|
| In-process library | `Harvest.Module` over the consumer's PostgreSQL | implemented locally |
| Self-hosted service | generic `Harvest.Api` image and `Harvest.Client` | implemented; rollout remains separate |
| Provided service | `Harvest.Client` pointed at the WoW2 instance | contract implemented; instance setup remains separate |

## Model

A tenant owns keys and private containers; a shared pool can serve every tenant. A source belongs to one container.
A pipeline freezes the source and adapter settings for each run, and schedules capture, discovery, refresh or checks.
Durable work items preserve the frontier when a worker stops. Captures support canonical posts with source links and
field evidence. A subscription owns an immutable event log that consumers read through an opaque cursor.

Generic readers cover sitemaps, JSON-LD events, RSS/Atom, ICS and ordinary pages. Deterministic rules supply dates,
prices, language and normalized contact numbers. A confirmed disappearance changes a link and the post's rolled-up
state; refusals never count as deletion evidence. Reviewed merges can be undone and kept apart.

## Source posture

Harvest fetches directly and politely: an honest user agent, robots policy, one paced host connection, conditional
requests and `Retry-After`. A challenge, block or rate limit becomes an outcome and a persistent pause. No CAPTCHA,
proxy/IP, account pool or provider integration belongs to this build. JavaScript-only pages report the unbound
rendering capability. AI stays off. Source-specific brokers remain in the private adapters repository.

## Boundaries

The public repository owns the generic engine, contracts, family readers, self-hosted image and reusable console.
The private adapters repository references these packages and builds WoW2's source-specific service images.
Social owns official platform integrations and never depends on Harvest. Secrets remain in the platform vault.

Runtime requirements are PostgreSQL 16+ with UTF-8 and `pg_trgm`, a connection string and an operator API key. No
additional external service account is required. The version track records the build; the backlog records later work.
