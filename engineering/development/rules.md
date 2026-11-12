# wow-two-sdk-beta.harvest — Engineering Rules

*Last updated: 2026-11-12*

> Working rules for agents in this repo — repo-specific deltas only. Shared rules: wow-two-ws/conventions/.

- Sourcing serves internal tracking only; nothing collected is republished, and anything shown carries facts plus an
  attributed link (events-integration #50). A change that would expose source text, photos or a feed breaks it.
- Sources are read directly, identified, inside `robots.txt` and paced; a challenge, block or rate limit pauses the
  source. Never add an automatic route change, a challenge submission or a rotating proxy pool.
- Optional capabilities are not dead code: Instagram, proxy/IP leases, supplied sessions and the CAPTCHA client are
  registered extensions that stay disabled until explicitly configured.
- A build, cleanup or passing fixture test implies no live provider request and no schedule activation. A parser
  fixture does not establish live access, a solver client does not establish a supported challenge flow, and a
  sign-in redirect does not establish an authenticated round trip; keep these apart in each verification note.
- Preserve the existing runtime database and local credentials during cleanup.
- A needed core SDK change goes to core first, then the pin moves here; a Harvest package change lands in `packages/`,
  never as a workaround in `sources/`. Backend deltas: [backend guidance](backend-guidelines.md).
