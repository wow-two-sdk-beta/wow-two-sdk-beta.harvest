# Posts/

The post store resolves one post from the links that show it. `IPostBroker` exposes capture writes, observations,
container-scoped reads, merge and unmerge; `AddHarvestPosts()` registers the PostgreSQL implementation.

| Implements | Over |
|---|---|
| link upsert by `(source_id, external_key)`, dedup tiers 0–2, `ResolvePost`, merge and undo (`move_link`), the link state machine and the post rollup | `Readers.Capture`, `Extraction.PostEnvelope`, `Steps.LinkObservation`; states `Posts.LinkState`, `Posts.PostState`, roles `Posts.LinkRole` |

- Owns: `harvest_posts`, `harvest_post_texts` (trigram GIN on `match_text`), `harvest_post_links`,
  `harvest_link_schedule`, `harvest_link_readings`, `harvest_link_events`, `harvest_link_check_log`, `harvest_captures`
  writes (monthly partitions; `harvest_ensure_capture_partitions` creates months ahead).
- Rules: a capture row only on a content-hash change; the post is derived from readings, never patched; only a
  completed run over a complete enumeration infers `suspect`; unverifiable links abstain.
- Emits, in the same transaction: `post.new`, `post.changed`, `link.state` rows through the Delivery lane's log.
- capture writes and subscription backfills share a container advisory transaction lock; a content projection's
  SHA-256 controls capture creation, so replaying a completed write appends neither a capture nor an event
- local capture bodies use `harvest_captures.body_inline` while Files is unbound; the retention policy determines
  whether raw bytes may be retained, and `expires_at` records their expiry
- dedup accepts exact canonical URLs and declared origins, or substantial equal content/text with matching dates,
  prices and contacts; different declared origins and ambiguous candidates remain separate
- field precedence favours live links, then source authority and capture recency; `PostModel.FieldSources` names the
  supporting link for each resolved field
- a first direct 404 marks a link suspect; another independent check confirms gone; 410 confirms immediately;
  a refusal never supplies negative evidence and a source with no check capability abstains
- a check's `(link_id, run_id)` is unique so a replay cannot count as another check; captures older than a later
  absence observation cannot revive the post
- merge records the exact moved link identifiers in `harvest_post_merges`; undo restores them and records the pair
  in `harvest_post_keep_apart`, preserving both post identities
