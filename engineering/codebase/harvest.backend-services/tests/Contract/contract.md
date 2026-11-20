# Contract/

Golden pages of SCFD's pull wire. `wow2.event.v1/` holds `GET api/subscriptions/{id}/events` answers exactly as
`HarvestSources.Api` serves them, one case per file, plus `subscription.json` (the subscription's own `GET`) and
`manifest.json` (`sha256` and case names per file). Events replays them to pin the wire it reads.

- Writer and check: `Harvest.Tests.E2E/Contract/EventContractPagesTests` runs the real host over a fresh database;
  without the switch it fails on the first byte that differs from these files.
- Regenerate, only for a deliberate wire change:
  `HARVEST_WRITE_CONTRACT=1 dotnet test tests/Harvest.Tests.E2E --filter FullyQualifiedName~EventContractPagesTests`,
  run from `harvest.backend-services/`
- The subscription is Ocharo's: `wow2.event.v1`, filter `ocharo-fashion-v1`, context `catalog=ocharo-fashion`, as
  `ocharo setup` provisions it; `subscription.json` carries the `filterKey` Events requires. Every post on a page
  passes the filter, and a concert saved before `01` shows a rejected post never appears.
- Pages chain: each pull sends the previous page's `next`; `04-empty` returns the cursor it was sent. No `02` exists;
  S17's `10-estimated` is `11-estimated`, since `10-unmatched` took the number first.
- Stable values: a fixed clock (`2026-11-10T09:00Z`, one minute per write); every Guid becomes
  `00000000-0000-7000-8000-0000000000NN` by first appearance, one numbering across the folder in file order;
  `itemId` is recomputed by the writer's rule `sha256("{postId:N}:{revision}:{type}")`; a cursor keeps
  `base64("{subscription:N}:{tx}:{row}")` with the transaction id as its ordinal. Nothing else is touched.
- `05-lifecycle` carries the post and link lifecycle a step emits; `post.relisted`, `pipeline.run` and
  `pipeline.health` have no producer yet. `08-purged` is a reserved type inserted with the writer's columns, no
  payload, revision + 1. `09-deadline` is a Craftfair-shaped call whose `deadlines` lists its closed application
  deadline. `10-unmatched` is a delivered post whose next revision the filter rejects: `post.new`, then
  `post.unmatched` at revision 2 carrying that revision. `11-estimated` is an undated show through the AI passes,
  answered by the scripted caller: `post.new` without `starts`, the tags' revision with a `relevant` classification
  (kind `event`), the `extract-event` revision with `starts.certainty=estimated` at month precision (kind `runway`), and
  a later capture stating the day as `starts.certainty=stated`. Every `starts` and `ends` carries `certainty` since S17.
- `13-image-copied` is a `post.new` whose post carries two images, the first already copied into Files: `media[0].fileRef` is
  the copy's file id and `media[1].fileRef` stays `null`; each `url` stays the source link. Every other page's media shows
  `fileRef: null`, since no copy exists for it.
- The host runs with the shared AI module on and Ocharo's wanted pair, tagging off; only case 11 runs the passes, for
  its one post, so no other case carries an answer.
- Events copies `wow2.event.v1/` unchanged and records the tag and commit it copied from.

## `wow2.event.v1/selector/`

Golden pages of a selector subscription, the same `GET api/subscriptions/{id}/events` wire with `match` on each event and the
payload's top-level `context`. Files: `subscription.json` (the subscription's own `GET`: `kind`, `name`, `selector`,
`selectorVersion`), `01-match.json`, `02-change.json` and `manifest.json` (`sha256` and case names per file, names relative
to the folder). The existing channel files keep their bytes; Events copies the folder with them.

- Writer and check: `Harvest.Tests.E2E/Contract/SelectorContractPagesTests`, run beside the channel pages by
  `HARVEST_WRITE_CONTRACT=1 dotnet test tests/Harvest.Tests.E2E --filter FullyQualifiedName~ContractPages`, from
  `harvest.backend-services/`; it runs the real host over its own fresh database and writes only this folder.
- The subscription: `ocharo-events`, schema `wow2.event.v1`, context `catalog=ocharo-fashion`, selector `entityTypes [event]`,
  `domains [style-fashion]`, one include ref to the container's source group with context `{"defaultKind":"exhibition"}`;
  one container, one group, one source.
- `01-match`: an event post the model tagged `type:event` and `domain:style-fashion/high-fashion` (with its ancestor, as
  the tags overlay writes it) arrives as `post.new`, reason `ingest`, `match {selectorVersion 1, verdict match, clauses
  [type:event, domain:style-fashion]}`, and `data.context` is `{catalog, defaultKind}`.
- `02-change`: the selector is replaced by one that adds `excludeDomains [style-fashion/high-fashion]`; the change re-judges the
  post the log holds on the same cursor, so it arrives as `post.unmatched`, reason `resync`, `match {2, noMatch,
  [exclude-domain:style-fashion/high-fashion]}`. The test runs the change in-process on the host's `IDeliveryBroker`, so the
  page never depends on an HTTP route.
- Stable values: the channel pages' rule with its own clock (`2026-11-20T09:00Z`, one minute per write) and its own Guid
  numbering from `…01`, and the item id of a change by the writer's selector rule,
  `sha256("{postId:N}:{revision}:{type}:s{selectorVersion}")`; `01` keeps the ingest rule
  `sha256("{postId:N}:{revision}:{type}")`. The folder's normalizer lives in the same test file.
