# Engine/

The run engine turns schedules into runs, freezes plans, claims work items, resolves each step's requirements and
calls the adapter's brokers.

| Implements (Abstractions) | How |
|---|---|
| `Steps.IStepContext` | one per claim: the resolved plan, a direct client with the run's cookie jar, the renderer when bound, `Observe` into the host gate |
| the caller side of `Steps.ISourceAdapter` · `IStepBroker` | plans from `AdapterDescriptor`, layered adapter → source → pipeline → step, frozen into `runs.frozen` |
| `Runs.*` vocabulary | statuses, triggers, reasons, item states, priorities |

- Owns: `harvest_schedules.next_due_at` and the automatic `harvest_schedules.enabled=false`, `harvest_pipelines.failures_since`
  and `auto_off_*`, `harvest_runs`, `harvest_work_item` (the frontier), lane backoff and usage attribution, and the operator's reading pace (`harvest_host_state.min_delay_ms`, `IRunBroker.SetReadingPaceAsync`:
  1000–60000 ms per resolved host; it returns the source's health from `Sources/`). An adapter may declare a
  `SourceUrl` and matching `HostKey` separately from the source identity; legacy host sources use their handle.
- Rules: claim by `(host_key, lane)` with `FOR UPDATE SKIP LOCKED` from two pools (the shared pool takes one running item per
  lane; the parallel pool takes up to a run's own `parallel` items); leases, never boot-time interrupts; fenced result
  writes; no transaction spans network I/O; `UNIQUE (pipeline_id, kind, scheduled_for)` makes scheduling idempotent.
- A challenge, block or rate limit stays the item's outcome: its lane backs off, and the item waits for the lane, never
  for another route.
- Adapters register in code (`AddHarvestBroker<T>()`); named-source brokers come from `sources/`, registered by
  `HarvestSources.Api`.

## Execution

- `AddHarvestEngine` registers `IRunBroker` and `HarvestEngine`; `AddHarvestWorker` explicitly starts the polling worker.
- two claim pools, both under the same row locks and lane pause checks: the shared pool claims for runs whose frozen stage has no
  `parallel` or `parallel` 1, one running item per host lane, counting only those runs' items; the parallel pool claims for runs
  with `parallel` above 1, while the run has fewer running items than its `parallel`, whatever else runs on the lane. The worker
  is a dispatcher: one shared slot, plus `Harvest:Worker:ParallelSlots` (default 16, 1–64) slots for parallel claims, each claim
  a task in its own scope; it claims again when a claim ends or the poll interval passes
- a run's stage settings are frozen with it: `walk`, `gapMs` and `parallel` read from `frozen.stage`, and a run that sets a gap
  or parallel fetches freezes `pace` into its work items' plan, which the fetch lane reads (`Capabilities/capabilities.md`)
- a discover run's walk: `resume` reads from its cursor under the pipeline's page, item and request budgets; `full` and `new`
  clear the cursor rows, start at the entry page and drop the pipeline's request budget (the byte and time budgets and the
  engine's own request ceiling still apply), so a `full` run ends `exhausted` at the last page; `new` also stops paging at the
  first catalog page that saved no new listing (`pending` for the first time, or reset to `pending` by a changed fingerprint)
- a harvest run takes at most its `BatchSize` pending items (1–500) and is not cut by the pipeline's request budget
- a scheduled harvest slot with no pending item queues no run and still advances `next_due_at`; a manual harvest records `no_items`
- the scheduler holds one transaction advisory lock, advances each stored due time past now and coalesces downtime
- interval/adaptive-minimum and five/six-field cron schedules use their IANA time zone and optional daily window
- each run freezes its source, pipeline, adapter and steps; query parameters are applied before work is queued
- work is claimed under row locks with a unique owner and an expiry; a dead worker's expired claim can be reclaimed
- network and extraction run outside transactions; completion locks the run and validates the current claim before writes
- capture persistence is idempotent if a worker dies after ingest but before frontier completion; negative checks deduplicate by run
- item limits apply across multi-item pages; frontier, request, byte, page and runtime limits stop remaining work
- `maxPages` bounds the pages a run whose plan reads listings admits, listing and detail pages alike; admitted pages
  finish within the other budgets and the run ends `page_limit`; a refresh or staged harvest counts listing pages only
- request/byte budgets are evaluated at step boundaries; one fetch includes robots and redirect transport attempts
- cancellation fences running claims; a late result cannot overwrite cancellation or a replacement owner's result
- a refusal terminates the run as partial and pauses its lane; the same run never changes route or automatically retries it
- a lane's pause doubles with each refusal in a row (`RefusalBackoff` × 2^n, n capped at 6); a successful network step on
  the lane resets the count, so the next refusal backs off from the first step
- `PipelineAutoOff` turns a pipeline's capture, check, refresh, discover and harvest schedules off after `AutoOffAfter` failed
  runs in a row (a pipeline's `auto_off_after` wins; 0 never): the count is read from `harvest_runs`, never stored, as the leading
  `failed` verdicts of `PipelineAutoOff.Verdict` (`skip` for `completed no_items`, cancelled and still-active runs, and a `partial`
  run with nothing saved and no failed step; `reset` for anything saved or any other `completed` run) among runs queued at or after
  `failures_since`; completion only reports its run's verdict, and the check runs after its commit (`AfterFailedRunAsync`, under the
  scheduler lock) or, for a run that failed at queue time, in the scheduler's own transaction; turning a row back on clears the
  record and sets `failures_since`
- a page result's follow-up targets are admitted in groups: details the container has no link for, listings and other
  targets, known details not read within the run's schedule interval (6 h with none), known details read since; each
  group keeps page order except the stale one, which goes least recently read first -- a page-limited run reaches new
  items before it re-reads known ones, and its stale items take turns
- a schedule's `last_queued_at` moves only when its slot inserted a run; a slot that finds the pipeline's run still
  active passes without a stamp
- known-link checks use their next-check time and preserve each link's external identity, including JSON-LD fragment IDs
- full-sweep absence inference, adaptive cadence tuning and retention maintenance are outside this slice
