# Harvest — product context

*Last updated: 2026-10-27*

## Current state

The direct v0.1 engine is implemented locally: fetching, durable runs, generic readers, deterministic extraction,
posts and provenance, deletion checks, pull delivery and the reusable console. Module, HTTP clients and test fakes
share conformance suites. Automated fixture and access checks pass; developer acceptance and live rollout remain open
in the version track. No live source harvest or production rollout is claimed by the offline fixture tests.

## Decisions

- 2026-10-27 — corrected the initial scope after user clarification: build configurable CAPTCHA, proxy/IP and
  account-session integrations in the private adapter host; credentials arrive later and providers start disabled.
- 2026-10-27 — keep direct HTTP as the public default; expose leased transports and reusable Hosting composition.
- 2026-10-27 — filter consumer delivery through host-registered predicates; preserve raw captures and lifecycle events.
- 2026-10-27 — challenges, blocks and rate limits are outcomes that pause the host lane, never deletion evidence.
- 2026-10-27 — keep named-source internals and WoW2 service images in the private adapters repository.
- 2026-10-27 — store normalized contact numbers as plain text and compare them directly for deduplication.
- 2026-10-27 — retain bounded capture bodies in PostgreSQL while Files integration is unbound.
- 2026-10-27 — compose the console from `fe-harvest/admin` in the shared platform chalk/forest style.
- 2026-10-27 — Events reuses Harvest pipelines and delivery; consumer prompts and schema selection remain its own.

## Acceptance

The version track retains the developer's manual checkboxes. Source-specific adapters, model extraction, rendering,
webhooks and retention maintenance remain separate work; exact boundaries live with each implementation lane.
