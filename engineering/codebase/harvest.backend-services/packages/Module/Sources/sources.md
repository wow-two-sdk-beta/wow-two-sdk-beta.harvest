# Sources/

The read side of the console's Sources area. `ISourceOverviewBroker` answers one overview per registered source of a
container, the posts one source shows, the posts the AI passes processed and a post's revisions; `AddHarvestSources()` registers the PostgreSQL
implementation. Source groups and display names are the registry's (`Registration/`), the reading pace the engine's
(`IRunBroker.SetReadingPaceAsync`).

| Implements (Abstractions) | Over |
|---|---|
| `Sources.ISourceOverviewBroker` → `SourceOverviewModel` (sections, last harvest, 14-day series, health), `SourcePostModel`, `SourceProcessedPostModel`, `Posts.PostRevisionModel` | sources and pipelines through `IRegistrationBroker`; reads `harvest_runs`, `harvest_post_links`, `harvest_captures`, `harvest_posts`, `harvest_post_revisions`, `harvest_subscriptions`, `harvest_host_state`, `harvest_host_lane`, `harvest_post_tags`, `harvest_post_extractions`, `harvest_taxonomy_nodes`; the AI module's `IInferencesBroker` when the host composes it |

- Owns: no table; it reads the lanes' tables, one query per table for the whole container.
- `state`: `needsAttention` while the host is paused, even with a run queued behind the pause; else `harvesting` while a
  run of the source is queued or running; else `needsAttention` when the host has failed 3 or more times in a row (host
  or lane) or the last capture run failed; else `healthy` when a section harvests; else `idle` (`SourceOverviewRules`).
- A section harvests when its pipeline is `active` and has an enabled capture schedule; `captureEvery` and `checkEvery`
  are the enabled rows' intervals, and `nextDueAt` the capture row's due time while the section harvests.
- `lastHarvest` is the newest capture run across the source's pipelines; a section's `lastRun` is its newest run of any
  kind; `at` is `finished_at`, else `queued_at`; `reason` is the stored snake_case `completion_reason`.
- `newPostsDaily` counts the source's links by `first_seen_at` over 14 UTC days, oldest first, today last.
- A post belongs to the section whose run made its earliest capture from the source; merged posts are left out.
- `imageUrl` is the source `url` of the first `image` in the post's `base.media`, omitted when there is none; a stored
  copy (`fileRef`) is never used, since no step writes one and no route serves it.
- Health: the planner resolves the host from adapter `SourceUrl`/`HostKey`, falling back to the source handle;
  publisher identities on one provider share that host. The pace is `harvest_host_state.min_delay_ms`
  (2000 ms with no row); a pause shows only while it lasts, the later of the host's and its lanes'; `lastRefusal` is the
  newest run whose fetch outcome is a refusal or a challenge.
- The pace is per host, so setting it for one source paces every source and container on that host.
- Processed (`ModuleSourceOverviewBroker.Processed.cs`): one query over the source's surviving posts that have a
  `harvest_post_tags` row — the done row about the current title-and-text hash (`PostTagsContent.SqlHash`), else the
  newest done, else the newest — with its domains labelled from `harvest_taxonomy_nodes` and the `extract-event` or
  `extract-listing` row of the tagged type about the same hash; the verdict is `base.classification`. Filters and the
  `(first_seen_at, id)` cursor follow `SourceProcessedQueryRules`. The route `{provider}/{model}` splits into provider
  and model; cost and latency come from one `IInferencesBroker.ListAsync` per post revision the page's answers name
  (subject `harvest:post:{id}@{revision}`), matched by `inference_id`; without the AI module both stay empty.
