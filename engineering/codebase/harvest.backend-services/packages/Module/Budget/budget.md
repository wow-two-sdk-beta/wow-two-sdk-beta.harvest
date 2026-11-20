# Budget/

The AI spend guard: one reading of what the host's model calls cost in the current window, so the passes slow down and then
stop before a provider credit runs out (`engineering/development/ai-operations.md` § Spend guard).

| Implements | Over |
|---|---|
| `IProcessingBudget` (`ReadAsync` → `BudgetReading`) · `ProcessingBudgetOptions` (`Harvest:Processing:Budget`) | the shared AI contract's inference log (`IInferencesBroker.TotalsAsync`); no table of its own |

- Owns no table and no migration: the spend is the sum of `CostMicroUsd` over every use case and data scope the AI module's log
  holds for the window, evals included.
- Window (`BudgetCalculator.Window`): the month that started on the latest `CycleStartDay` (1 to 28) at 00:00 UTC, or
  `[SinceUtc, now)` when `SinceUtc` is set, which never resets. The log is read up to `now + 1 s`.
- State (`BudgetCalculator.Classify`): `off` without a `LimitUsd` (or with no log composed), `hard` at spent ≥ limit, `soft` at
  spent ≥ `limit × SoftShare`, else `open`. The projection is spent ÷ elapsed × the window's length, monthly windows only.
- `ProcessingBudget` is a singleton: it reads the scoped log in a scope of its own, keeps a reading for `RefreshInterval`
  (one refresh at a time), keeps the last good reading when a read fails, and with none fails closed at `hard`
  (`ReadFailed`) when a limit is set. It never throws into a worker and logs each change of state once.
- Who gates on it: `PostTagsBroker` and `PostExtractionBroker` (`ProcessDueAsync` claims nothing at `hard`; a call takes
  `BudgetReading.Routes` — the last route only at `soft`; an operator `RunAsync` is refused with `Conflict` at `hard`),
  `RentalTermsV2Processor` in `sources/Module` (no model call at `hard`) and `PostReprocessingBroker` (a dry run carries the
  reading). The host reads it through `processing budget` and `GET api/processing/budget`.
- `AddHarvestProcessingBudget()` is called by the tags, extraction and re-run registrations; the host binds the section once.
