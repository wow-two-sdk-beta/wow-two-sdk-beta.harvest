# Engine/

The run engine turns schedules into runs, freezes plans, claims work items, resolves each step's requirements and
calls the adapter's brokers.

| Implements (Abstractions) | How |
|---|---|
| `Steps.IStepContext` | one per claim: the resolved plan, a direct client with the run's cookie jar, the renderer when bound, `Observe` into the host gate |
| the caller side of `Steps.ISourceAdapter` · `IStepBroker` | plans from `AdapterDescriptor`, layered adapter → source → pipeline → step, frozen into `runs.frozen` |
| `Runs.*` vocabulary | statuses, triggers, reasons, item states, priorities |

- Owns: `harvest_schedules.next_due_at`, `harvest_runs`, `harvest_work_item` (the frontier), lane backoff and usage attribution.
- Rules: claim by `(host_key, lane)` with `FOR UPDATE SKIP LOCKED`; leases, never boot-time interrupts; fenced result
  writes; no transaction spans network I/O; `UNIQUE (pipeline_id, kind, scheduled_for)` makes scheduling idempotent.
- A challenge, block or rate limit stays the item's outcome: its lane backs off, and the item waits for the lane, never
  for another route.
- Adapters register in code (`AddHarvestBroker<T>()`); named-source brokers come from the private adapters repo.

## Execution

- `AddHarvestEngine` registers `IRunBroker` and `HarvestEngine`; `AddHarvestWorker` explicitly starts the polling worker.
- the scheduler holds one transaction advisory lock, advances each stored due time past now and coalesces downtime
- interval/adaptive-minimum and five/six-field cron schedules use their IANA time zone and optional daily window
- each run freezes its source, pipeline, adapter and steps; query parameters are applied before work is queued
- work is claimed under row locks with a unique owner and an expiry; a dead worker's expired claim can be reclaimed
- network and extraction run outside transactions; completion locks the run and validates the current claim before writes
- capture persistence is idempotent if a worker dies after ingest but before frontier completion; negative checks deduplicate by run
- item limits apply across multi-item pages; frontier, request, byte, page and runtime limits stop remaining work
- request/byte budgets are evaluated at step boundaries; one fetch includes robots and redirect transport attempts
- cancellation fences running claims; a late result cannot overwrite cancellation or a replacement owner's result
- a refusal terminates the run as partial and pauses its lane; the same run never changes route or automatically retries it
- known-link checks use their next-check time and preserve each link's external identity, including JSON-LD fragment IDs
- full-sweep absence inference, adaptive cadence tuning and retention maintenance are outside this slice
