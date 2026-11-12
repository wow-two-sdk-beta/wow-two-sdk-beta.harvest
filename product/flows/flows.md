# flows/

Product flows; a flow that grows past a few steps gets its own `{flow-slug}.md`, indexed here. Technical flows:
`engineering/architecture/flows/flows.md`.

## Operator: provision and run sources

1. Exchange the operator's service credential for a scoped SCFD token (or use the local operator key).
2. Review the installed sources and their capability metadata: `GET /api/ocharo/sources/registry`,
   `GET /api/sources/{id}/capabilities`.
3. Provision Ocharo's channels with `ocharo setup --group <id>` or `POST /api/ocharo/sources/setup`; schedules stay
   off until asked. OLX sources are registered through the registry API with a saved category or search URL.
4. Run a pipeline explicitly and inspect its counts, evidence and refusals; confirm bounded runs and tenant isolation
   before enabling schedules.

## Consumer: pull a subscription

1. Exchange the consumer's own client credential for a token bound to its SCFD tenant.
2. Pull `GET /api/subscriptions/{id}/events` from the stored cursor and apply each post's highest revision.
3. Events classifies Ocharo's `wow2.event.v1` candidates into Ocharo's private catalog. Haven Supply owns intake,
   classification and triage of OLX listings (`wow2.post.v1`).
