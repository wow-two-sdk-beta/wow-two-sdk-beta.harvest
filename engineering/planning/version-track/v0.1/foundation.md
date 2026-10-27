# v0.1 · Iteration 1 — Foundation (transient plan)

*Last updated: 2026-10-27*

> What iteration 1 builds and how; deleted when the iteration ships. Sources: wow-two-ws/ideas/harvest-analysis-2026-10
> T5, T7, T8 § 3 and § 7, T9 § 1.5, § 3, § 4.4 and § 8 (developer-confirmed 2026-10-27).

## What

- the Greetings sample leaves every package, the service, the tests, `fe-harvest` and `apps/web`
- PostgreSQL store (Npgsql) with the bespoke SQL migrator, SQL `001` holding the full slice-1 schema
- boot self-test: UTF-8 database encoding, `pg_trgm` present, `show_trgm('привет')` non-empty; boot fails otherwise
- the registration contract: tenants, API keys, containers, sources, pipelines with steps, schedules and scopes
- the engine contract later lanes build against: steps, capabilities, readers, extraction, delivery
- registration over HTTP (service) and its mirror (Client); fake and conformance suite (Testing)

## How

- core pin `10.0.77-beta` → `10.0.81-beta`: named migration sets keep the module's history apart from a Form 1
  consumer's (`harvest_migration_history`, own advisory lock); `Identity/ApiKeys` still ships in core there
- tables in `public`, `harvest_` prefix, snake_case, names as the tracks spell them; ids are UUIDv7 made in code
- `harvest_captures` range-partitioned by month on `captured_at`, a default partition, and
  `harvest_ensure_capture_partitions(months_ahead)` that `001` calls for three months and the maintenance job reruns
- `AddHarvest()` registers the store on its own keyed Npgsql data source, the `harvest` migration set, the self-test
  and `IRegistrationBroker`; `MigrateHarvestOnStartupAsync()` creates the database, checks, migrates, checks again
- API keys mint and hash with core `ApiKeySecretFactory`, marker `hv_`; one configured operator key holds every scope;
  tenant keys hold tenant scopes only, and non-operator keys reach only their tenant's containers
- the contract stays BCL plus the kernel: egress, session, profile and challenge types reference no Harvest type, so
  they move to core `Http/Egress` by a namespace change
- Module folders `Capabilities/` · `Engine/` · `Readers/` · `Extraction/` · `Posts/` · `Delivery/` hold a lead doc only;
  the schema in `001` is the shared seam between lanes

## Tests

- Unit: normalization, scope keys, schedule and step-override rules, self-test verdicts, client error mapping
- Integration: the module over PostgreSQL (Testcontainers `postgres:16-alpine`), self-test pass and refusal
- Migrations: `001` apply, idempotency, rollback, re-apply; partitions; extension
- Conformance: `RegistrationBrokerConformance` against Module, Client (in-process host) and the fake
- E2E: the host over PostgreSQL: wire, auth, boot refusal, `/health`, pages, the descriptor's dev check

## Out

- engine, fetching, readers, extraction, posts, delivery behaviour (iterations 2–7); the console (iteration 8)
- `Tenancy:Mode`, budgets and caps, `source_routes`, `publishers`, the purge role and grants
