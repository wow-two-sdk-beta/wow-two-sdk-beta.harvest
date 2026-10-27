# Posts/

The post layer — iteration 6: one improved post per thing, with every link that shows it.

| Implements | Over |
|---|---|
| link upsert by `(source_id, external_key)`, dedup tiers 0–2, `ResolvePost`, merge and undo (`move_link`), the link state machine and the post rollup | `Readers.Capture`, `Extraction.PostEnvelope`, `Steps.LinkObservation`; states `Posts.LinkState`, `Posts.PostState`, roles `Posts.LinkRole` |

- Owns: `harvest_posts`, `harvest_post_texts` (trigram GIN on `match_text`), `harvest_post_links`,
  `harvest_link_schedule`, `harvest_link_readings`, `harvest_link_events`, `harvest_link_check_log`, `harvest_captures`
  writes (monthly partitions; `harvest_ensure_capture_partitions` creates months ahead).
- Rules: a capture row only on a content-hash change; the post is derived from readings, never patched; only a
  completed run over a complete enumeration infers `suspect`; unverifiable links abstain.
- Emits, in the same transaction: `post.new`, `post.changed`, `link.state` rows through the Delivery lane's log.
