# Reprocessing/

Operator controls over the AI passes (`post-tags`, `extract-event`, `extract-listing`): re-runs, their progress and eval
sets from stored posts, so a prompt can change and its answers be re-run and re-evaluated safely
(`engineering/development/ai-operations.md`).

| Implements | Over |
|---|---|
| `IPostReprocessingBroker` (re-run, status, eval set) | the Tagging and PostExtraction lanes' queue, gates, options and SQL (`PostTagsStore.ModeSql`, `AutoSql`); the shared AI contract's inference log (`IInferencesBroker`) and eval bench (`IEvalsBroker`) |

- Owns: `harvest_processing_runs`, and the `run_id` and `explicit` columns of `harvest_post_work` (017).
- Re-run (`ReprocessingStore.QueueAsync`): one statement per use case, tags first, inside one transaction that records
  the run and its counts. The scope's current posts with a title or text, the since and prompt-version filters, for
  extraction the use case's entity type (and, outside named posts, the demand's pairs), minus the posts the same run
  queued for tags. A post runs when its strictest mode is `auto`, or not `off` for named posts and listed sources (an
  explicit choice; only a run over every source leaves `manual` out); a row a worker holds stays. A queued row replaces a waiting one, takes the run and starts its attempts over.
- Dry run: the same transaction rolled back, so its counts are exact. The estimate prices each use case from the
  newest production inferences of 30 days on its first configured route, summed per subject and averaged.
- Status: the counts the run queued with (`harvest_processing_runs.counts`) and the run's rows still in the queue per
  kind — waiting, held, retrying with their last errors. Downstream rows (extraction after tags, entity resolution after
  extraction) carry the run too; the run is done when none remains.
- Eval set: creates the set when missing (an existing one of another use case is refused), draws a seeded sample
  (`md5(seed || post id)`) of current posts whose sources are not `off` (for extraction, tagged with the use case's
  entity type; for `current`, with a done answer), and adds one item per post not yet in the set, subject
  `harvest:post:{id}@{revision}`, tagged `unreviewed`. The AI module resolves each input through the Tagging lane's
  subject source.
- Writes no post and no answer itself: the passes' workers do, as for any queued row.
