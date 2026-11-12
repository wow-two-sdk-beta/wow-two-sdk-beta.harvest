# SCFD product context

*Last updated: 2026-11-12*

## Current state

The direct v0.1 engine is implemented: fetching, durable runs, generic readers, deterministic extraction, posts and
provenance, deletion checks, pull delivery and the reusable console. Module, HTTP clients and test fakes share
conformance suites. The named readers (Afisha, ANONS, CAEx, Craftfair/Hunarmand, Uzexpocentre, OLX), the Ocharo
registry, setup and filter, and the opt-in Instagram and provider integrations build into the same image through
`HarvestSources.Api`. Fixture tests establish no live source access; developer acceptance stays in the version track.

The dev SCFD runs this image. Ocharo's two Afisha pipelines (fashion, exhibitions) capture every 6 hours with a daily
link check, and Events pulls both `wow2.event.v1` subscriptions. No proxy, CAPTCHA, session or Instagram provider is
configured; no paid call, account login or application has been made. Prod rollout remains open.

The shared host supports optional Identity BFF sign-in alongside service tokens and API keys. An explicit
organization/tenant binding and namespaced grants authorize browser access; organization owners have no global
operator authority. Browser writes require the session's CSRF token. The host persists tickets in PostgreSQL and
requires durable data-protection keys. The console is not served on dev yet (the site routes only `/api`).

## Decisions

- 2026-10-27 — build configurable CAPTCHA, proxy/IP and account-session integrations as source-host extensions;
  credentials arrive later and providers start disabled.
- 2026-10-27 — keep direct HTTP as the default; expose leased transports and reusable Hosting composition. The source
  host composes Hosting rather than copying its controllers or authentication.
- 2026-10-27 — filter consumer delivery through host-registered predicates; preserve raw captures and lifecycle events.
  Only matching event candidates enter an Ocharo subscription; existing lifecycle updates stay deliverable.
- 2026-10-27 — challenges, blocks and rate limits are outcomes that pause the host lane, never deletion evidence.
  Source lists are windows; absence from a window is no deletion evidence. Uzexpocentre row checks stay off, since
  the shared calendar URL cannot prove a row still exists.
- 2026-10-27 — Afisha first, then three complementary sources, with no claim of a top-volume ranking. Keep local,
  regional and international opportunities; keep visitor, vendor, exhibitor, runway and artisan evidence apart.
- 2026-10-27 — setup starts no schedules by default, preserves existing operator choices and issues no tenant secret.
- 2026-10-27 — store normalized contact numbers as plain text and compare them directly for deduplication.
- 2026-10-27 — retain bounded capture bodies in PostgreSQL while Files integration is unbound.
- 2026-10-27 — compose the console from `fe-harvest/admin` in the shared platform chalk/forest style.
- 2026-10-27 — Events reuses Harvest pipelines and delivery; consumer prompts and schema selection remain its own.
- 2026-11-10 — sourcing is for internal use only; nothing sourced is republished (events-integration #50). No person
  accepts sourced events; Events classifies them (#53), so subscriptions carry no `review` context.
- 2026-11-12 — merged `wow-two-sdk-beta.harvest-adapters` into this repository (events-integration #63): SCFD serves
  internal use only and authenticates every caller, so the source readers need no private repo. One solution, one
  image, one Wheelhouse product `harvest`; the source pin, the image build that downloaded public Harvest and the
  separate workflows are gone. A fixed outgoing route per source is allowed; rotating proxy pools stay out.

## Acceptance

The version track retains the developer's manual checkboxes. ANONS list access returned 403 on one direct request;
its reader is fixture-verified and unattended list access stays unconfirmed. Model extraction, rendering, webhooks
and retention maintenance remain separate work; exact boundaries live with each implementation lane.
