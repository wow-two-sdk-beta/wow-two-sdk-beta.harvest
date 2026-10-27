# Engine/

The run engine — iteration 3. Turns schedules into runs, plans and claims work items, resolves each step's capabilities
and calls the adapter's brokers.

| Implements (Abstractions) | How |
|---|---|
| `Steps.IStepContext` | one per claim: the resolved rung, leases, a client bound to them, `Observe` into the ladder |
| the caller side of `Steps.ISourceAdapter` · `IStepBroker` | plans from `AdapterDescriptor`, layered adapter → source → pipeline → step, frozen into `runs.frozen` |
| `Runs.*` vocabulary | statuses, triggers, reasons, item states, priorities |

- Owns: `harvest_schedules.next_due_at`, `harvest_runs`, `harvest_work_item` (the frontier), `harvest_enumerations`.
- Rules: claim by `(host_key, lane)` with `FOR UPDATE SKIP LOCKED`; leases, never boot-time interrupts; fenced result
  writes; no transaction spans network I/O; `UNIQUE (pipeline_id, kind, scheduled_for)` makes scheduling idempotent.
- Adapters register in code (`AddHarvestBroker<T>()`); named-source brokers come from the private adapters repo.
