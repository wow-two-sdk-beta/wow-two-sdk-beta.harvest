# SCFD

*Last updated: 2026-11-12*

## Problem

Products need the same source collection, scheduling, capture history, duplicate resolution, deletion checks and
delivery. SCFD owns that state once. A consumer subscribes to a pipeline and applies its highest post revision.
Events is the first consumer: it pulls Ocharo's event subscriptions in the deterministic `wow2.event.v1` projection
rather than own a second harvesting engine. OLX listings deliver as `wow2.post.v1` for Haven's intake.

Ocharo needs a reviewable stream of events where the brand may exhibit, sell or show its garments: Uzbekistan beyond
Tashkent, regional opportunities and international calls. An event announcement, visitor ticket or accessible web
page does not prove exhibitor, vendor, runway or artisan admission.

## Forms

| Form | Artifact | State |
|---|---|---|
| In-process library | `Harvest.Module` over the consumer's PostgreSQL | implemented locally |
| Self-hosted service | the `harvest` image and `Harvest.Client` | implemented; no self-hosted consumer yet |
| Provided service | `Harvest.Client` or the HTTP API pointed at the WoW2 instance | the dev SCFD serves Events; prod rollout remains separate |

## Model

A tenant owns keys and private containers; a shared pool can serve every tenant. A source belongs to one container.
A pipeline freezes the source and adapter settings for each run, and schedules capture, discovery, refresh or checks.
Durable work items preserve the frontier when a worker stops. Captures support canonical posts with source links and
field evidence. A subscription owns an immutable event log that consumers read through an opaque cursor.

Generic readers cover sitemaps, JSON-LD events, RSS/Atom, ICS and ordinary pages. Deterministic rules supply dates,
prices, language and normalized contact numbers. A confirmed disappearance changes a link and the post's rolled-up
state; refusals never count as deletion evidence. Reviewed merges can be undone and kept apart.

## Sources

- Afisha is first by the owner's decision. ANONS adds local market announcements, Craftfair/Hunarmand artisan calls
  and regional coverage, Uzexpocentre the venue's fashion and textile trade calendar; CAEx is selectable explicitly.
  These are complementary high-fit sources, not a measured ranking of publishers by volume.
- OLX supplies marketplace listings from saved category and search URLs, with stable ad IDs.
- Instagram reads allowlisted professional publishers through Meta's official Business Discovery API; opt-in.
- The reviewed Ocharo registry ranks 100 publishers and 189 channels; only channels with an installed reader can be
  provisioned. [Registry review](../engineering/research/ocharo-source-registry.md)
- Named readers keep source text, explicit event and publication dates, locations, application deadlines, access
  evidence and unknown fees. They never infer a new edition from recurrence or take a registration countdown as the
  event start.
- Ocharo's `ocharo-fashion-v1` delivery filter selects candidates; SCFD never applies, buys, registers or publishes.

## Use

- Collected records and captures serve the subscribing products' internal tracking only: Ocharo tracks events for
  itself (events-integration #50).
- Nothing sourced is republished, whether on a public site, in a feed or through another product. Anything shown
  inside Ocharo carries facts (title, dates, place) and an attributed link to the original, never the source's text
  or photos.
- Events pulls the Ocharo subscriptions into Ocharo's private catalog; its classification accepts or holds each record,
  no person reviews it (events-integration #53), and nothing reaches a public surface.
- Each named source's reading rules (discovery scope, identity, evidence limits) live in its folder's lead doc under
  `engineering/codebase/harvest.backend-services/sources/`.

## Source posture

SCFD fetches directly with an identified user agent, `robots.txt`, one paced connection per host, conditional
requests and `Retry-After`. A challenge, block or rate limit becomes an outcome and a persistent pause, never deletion
evidence. Afisha, ANONS, CAEx, Craftfair and Uzexpocentre are read directly. Proxy/IP routes, operator-supplied
account sessions, the CAPTCHA client and Instagram ship in the image and stay disabled until an operator configures
them. A configured route stays fixed for its source origin; rotating proxy pools to get past a site's limits stay out
(events-integration #50, #63). Generic fetching never changes routes automatically or submits a challenge; the
service neither creates accounts nor logs in. JavaScript-only pages report the unbound rendering capability. AI stays
off.

## Boundaries

This repository owns the engine, contracts, generic and named readers, provider integrations, the `harvest` image
and the reusable console. Social owns official platform integrations and runs independently of SCFD. Secrets live in
the platform vault.

Runtime requirements are PostgreSQL 16+ with UTF-8 and `pg_trgm`, a connection string, and an operator API key
locally or Identity-issued service access on a shared target. No provider account is required until its integration
is enabled. Live scheduling, provider activation and production rollout are separate operator actions. The version
track records the build; the backlog records later work.
