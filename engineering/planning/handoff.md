# Harvest — cloud handoff

*Written 2026-10-27. Resume point for a cloud session continuing the v0.1 build.*

## What this is

Harvest is a shared 24/7 harvester: on a schedule, it fetches what sources publish (pages, sitemaps, feeds,
official-provider APIs), extracts records into a schema the consumer names, dedups them, checks for deletions, and
delivers them. Grown from ListingShelf; Events is its first consumer. It ships in the three capability forms (library,
self-hosted image, WoW2-provided instance).

Full spec: `product/harvest-service-spec.md` in this repo (copied from the hub `ideas/harvest-service-spec.md`). The
hub spec is the live source; several hub edits from 2026-10-27 are **committed locally but not yet pushed** (the
developer pushes the hub in GitKraken), so if the copy here lags, the hub wins.

## The three repos

| Repo | Visibility | Holds |
|---|---|---|
| `wow-two-sdk-beta.harvest` (this) | public | the engine: steps, scheduler, runs/stats, post containers, dedup, extraction, subscriptions, delivery, retention, and the **generic family readers** (RSS, ICS, JSON-LD `Event`, sitemap, link cards) |
| `wow-two-sdk-beta.social` | public | the org's own platform integrations: publish with receipts, own metrics, verification, official-API transport. Social never depends on Harvest |
| `wow-two-sdk-beta.harvest-adapters` | **private** | one broker package per named source; builds the **actual deployed images**. Scaffolded from the domain template 2026-10-27 (`3c730c2`), template only |

**Public vs private split (developer 2026-10-27):** a public repo holds only what can be built in the open and what
should be public. The private repo holds the actual services and builds the deployed images, **referencing** the public
packages, not copying them. One-image-per-repo is not a constraint — a repo may host several services. So WoW2's own
Harvest image is built from `harvest-adapters`; the public `harvest` image stays generic.

**Chain:** core → Identity → Files → Social → Harvest → Events. `Harvest.Module` references core and
`Social.Abstractions` only.

### The adapters repo is the developer's to fill

`harvest-adapters` holds the per-source broker internals. Those internals are **out of scope for this handoff and for
the public repos** — do not build them here. This repo (`harvest`) stays generic: it defines the broker contract
(`Harvest.Abstractions`) and ships the generic readers that work on any cooperating source. A named-source broker is
registered in a host in code (`AddHarvestBroker<T>()`), never bound by name from operator config.

## Posture at sources — the firm line

Harvest fetches **politely and directly**, and treats a refusal as a result:

- obeys `robots.txt`, a per-host crawl delay, `Retry-After`, and conditional requests (ETag / `If-Modified-Since`);
- one connection per host, an adaptive `min_delay` (≥ 2 s), a `crawl_delay` ceiling of 30 s;
- records a block, rate limit or challenge as a **`FetchOutcome`** (`blocked` · `rateLimited` · `challenge` ·
  `waitingRoom` · `robotsDisallowed` · …) and **backs the run off**. A block is reported, never worked around;
- honours each platform's terms on **official API routes**;
- may render a page headlessly through the core render companion **to read it**, never to defeat a defence.

There is **no** escalation ladder, proxy/IP rotation to evade blocks, captcha solving, account pool for scraping,
disguised client profile, or per-site bypass recipe in this repo or its plan. Those were removed from the engine and
marked not-pursued in the hub analysis on 2026-10-27. Keep it that way here.

## State

- **Branch:** `main`. **HEAD:** `21815c0` (pushed). `main` is protected (no force push, no deletion).
- **Iteration 1 (Foundation): done and green.** Boot on PostgreSQL with schema applied; refuse boot without trigram or
  UTF-8; register tenants, API keys, containers, sources; declare pipelines with steps, schedules, scopes.
- **Schema:** `001-baseline` (26 `harvest_*` tables, monthly-partitioned `harvest_captures`,
  `harvest_ensure_capture_partitions(n)`); `002-drop-escalation` dropped the rung/lease/egress columns, kept
  `harvest_host_lane` for pause/failure/concurrency state.
- Contact numbers: stored as **plain text**, normalized to E.164, matched directly for dedup (developer 2026-10-27).
  Masked before any model sees text unless a schema sets `needsContacts`.
- Secrets: the **Secrets Vault** (platform app), not this repo.

## Build plan — `engineering/planning/version-track/v0.1/v0.1.md`

Iterations 2–9 are the remaining `v0.1` work; each line is one "Ability to…". Build in order; lanes inside an
iteration (e.g. the readers in iteration 4) can run as parallel agents on disjoint `packages/Module/{Lane}/` folders.

- **2 Fetching** — per-host pacing + robots policy; `Retry-After` + conditional requests; tell a challenge from a block
  from a rate limit. Direct fetching only. The configurable User-Agent is not built yet.
- **3 Engine** — run pipelines on schedule as claimed step work; resume a sweep after a worker dies; sweep a whole site
  from its sitemap; re-check known links on their own schedule.
- **4 Reading** — JSON-LD events; RSS and ICS; plain pages with links and media.
- **5 Extraction** — rules extraction (dates, prices, language); contact numbers as normalized plain text;
  schema.org `Event` → `wow2.event.v1`.
- **6 Posts** — merge one post seen through several links; show which link supplied each field; undo a wrong merge;
  mark a removed post suspect, then gone.
- **7 Delivery** — a product subscribes to a pipeline; pull new/changed posts from a resumable cursor.
- **8 Console** — manage pipelines/sources/subscriptions; watch runs, queue, host health live; browse posts with
  links and field sources. **Build it in Wheelhouse's applied style** (chalk/forest, glass bar, panels); the developer
  reviews and refines the UI afterward.
- **9 Verification** — harvest the mstar.uz and voyage.uz fixtures, each event arrives once; remove a fixture page, its
  post turns suspect then gone.

Backlog beyond v0.1: `engineering/planning/backlog.md` (Features · Sources · Delivery · Release).

## Events (consumer)

Events shows the events Harvest gathered, so it reuses the **same pipelines, delivery and console layout**; it differs
only in its **AI prompts**, tuned for events rather than goods/services posts. A consumer sets prompts per subscription
(`AiMode` + a prompt profile), not by owning a second engine. User-sent links, a batch, or an uploaded link file all
enter through Harvest intake the same way for every consumer. Events lives in its own repo
(`wow-two-sdk-beta.events`, spec `ideas/events-service-spec.md` in the hub) as a thin consumer.

## Conventions a cloud session must hold

- **Git:** subject-only commit messages, `{type}: {past-tense verb} {what}`, 50–70 chars. Use `git -C <literal
  absolute path>` (a guard rejects variables and `-m` bodies). Check `git var GIT_AUTHOR_IDENT` is
  `sultonbek.rakhimov@gmail.com` before each commit. Stage explicit paths. One commit per cohesive change.
- **SDK loop:** a product blocked on a missing/wrong SDK API fixes the SDK, never works around it. After an SDK push,
  wait for CI's release commit, `git pull --ff-only`, then re-pin.
- **Verify, don't claim:** report what ran (e.g. "49 tests pass") or "not run"; never "should work".
- **Agentic lane rule:** edit only your lane's files; a break rooted outside your lane is a hand-off, not a repair.

## Hand steps still the developer's

- Push the hub (`wow-two-ws`) in GitKraken — several 2026-10-27 commits are unpushed (spec + analysis + this session's
  catalog/ports/scaffold-fix).
- NuGet trusted-publishing policy and npm's first version, after the content gate (per the `create-library` plan).
- Wheelhouse registration and secret values.
- Drop the Codex app off the `wow-two-sdk-beta` org.
