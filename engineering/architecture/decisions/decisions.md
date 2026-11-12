# decisions/

One ADR per decision, `NNNN-{slug}.md`, once a decision needs more than a line; this lead doc lists them. Decisions
that span capabilities live in wow-two-ws/docs/platform-decisions.md. Dated product decisions: `product/context.md`.

- One repository owns the contracts, engine, persistence, HTTP API, console and the named source readers; one image
  ships them (2026-11-12, events-integration #63).
- The NuGet family and `fe-harvest` stay the published surface; the source readers are not packages.
- The source host composes Hosting rather than copying its controllers or authentication; `Harvest.Api` stays the
  generic host the E2E and Conformance tiers boot.
- Provider routes and supplied sessions are explicit, origin-scoped and disabled by default; a route stays fixed per
  source origin, never a rotating pool past a site's limits.
- Ocharo setup preserves operator choices and starts no schedule unless asked; candidates go to Events, which
  classifies them (events-integration #53).
