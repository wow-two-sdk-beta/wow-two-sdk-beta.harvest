# Contract/

Golden pages of SCFD's pull wire. `wow2.event.v1/` holds `GET api/subscriptions/{id}/events` answers exactly as
`HarvestSources.Api` serves them, one case per file, plus `subscription.json` (the subscription's own `GET`) and
`manifest.json` (`sha256` and case names per file). Events replays them to pin the wire it reads.

- Writer and check: `Harvest.Tests.E2E/Contract/EventContractPagesTests` runs the real host over a fresh database;
  without the switch it fails on the first byte that differs from these files.
- Regenerate, only for a deliberate wire change:
  `HARVEST_WRITE_CONTRACT=1 dotnet test tests/Harvest.Tests.E2E --filter FullyQualifiedName~EventContractPagesTests`,
  run from `harvest.backend-services/`
- Pages chain: each pull sends the previous page's `next`; `04-empty` returns the cursor it was sent. No `02` exists.
- Stable values: a fixed clock (`2026-11-10T09:00Z`, one minute per write); every Guid becomes
  `00000000-0000-7000-8000-0000000000NN` by first appearance, one numbering across the folder in file order;
  `itemId` is recomputed by the writer's rule `sha256("{postId:N}:{revision}:{type}")`; a cursor keeps
  `base64("{subscription:N}:{tx}:{row}")` with the transaction id as its ordinal. Nothing else is touched.
- `05-lifecycle` carries every type a step emits today; `post.relisted`, `pipeline.run` and `pipeline.health` have no
  producer yet. `08-purged` is a reserved type inserted with the writer's columns, no payload, revision + 1.
- Events copies `wow2.event.v1/` unchanged and records the tag and commit it copied from.
