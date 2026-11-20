# Subscription selectors

A selector subscription follows Layer 1 tags and sources over one container instead of one pipeline: entity types, taxonomy
domains, topics, timeframes and source groups, as data a client edits. The `subscriptions` host verb lists, changes,
previews, diffs, explains and adopts them from the deployed host, so the shadow period and the consumer's cutover run
through Wheelhouse Run with no operator token.
Contract: `../architecture/contract.md` § Subscription selectors (wire, limits, evaluation, deliverability, operations).
Code: `packages/Abstractions/Delivery/` (selector, rules, matcher), `packages/Module/Delivery/` (delivery, preview,
operations, extraction demand), `service/HarvestSources.Api/Subscriptions/SubscriptionsVerb.cs` (the verb).

## What the selector needs

A selector judges the Layer 1 tags a post revision carries (`type:`, `domain:`, `topic:`, `timeframe:`), so a post is
**pending**, delivered nowhere, until those tags exist:

- `Harvest__Processing__Ai__Enabled` and `Harvest__Processing__Tags__Enabled` are on (`post-tags.md` § Host settings).
- The sources it follows are Model processing `auto` (`ai-operations.md`): `sources processing --list --tenant <slug>`
  shows the mode, `ocharo setup … --model-processing auto` sets it.
- `post-tags backfill` has queued the posts stored before tagging was on, and `post-tags report` shows the queue empty.
- Extraction demand: each active selector adds an (entity type × domain include) pair beside
  `Harvest__Processing__Extraction__Wanted__N__*`, so no new setting is needed. A change queues the extractions for its
  new pairs (`extraction.md`).

## Running the verb

`dotnet HarvestSources.Api.dll subscriptions …` runs on the target's own settings through Wheelhouse Run, with no listener
and no operator token. stderr carries the report; the last stdout line is one JSON object. Exit `0` done, `1` refused or not
found, `2` usage (a malformed option, a `--set` that is not a selector document, a date that is not a date included).

```sh
dotnet HarvestSources.Api.dll subscriptions list --tenant ocharo
dotnet HarvestSources.Api.dll subscriptions diff --subscription ocharo-events --tenant ocharo --against ocharo-fashion-v1 --since 2026-09-01 --limit 40 --pending
```

- `--subscription` takes an id or a name. A name takes the one non-archived holder, so a retired subscription never
  shadows its successor; a name two tenants hold needs `--tenant <slug>`, and an id from another tenant than `--tenant`
  is refused.
- Wheelhouse keeps a last output line whole only up to 16 KiB. The verb says so on stderr when a result line is longer;
  lower `--limit` or `--samples` then. A diff item runs from about 350 bytes to well over 600 with full-length titles, so
  the default 50 can overflow; the shadow procedure below uses 30 and 40.

## Verbs

| Verb | Does |
|---|---|
| `list [--tenant <slug>]` | one stderr row per subscription, `<id> <kind> <name\|-> <filterKey\|selector vN> <state>`; `{ subscriptions: [...] }` with tenant slug, kind, name, filter key, selector version, pipeline, schema, state |
| `show --subscription <id\|name>` | the subscription as the broker reads it, selector included: `{ tenant, subscription }` |
| `selector --subscription … [--set '<selector json>'] [--since <yyyy-mm-dd>] [--by <who>]` | without `--set` prints the stored selector and version; with it, changes the selector on the same cursor -> § Changing a selector |
| `state --subscription … --state active\|suspended\|archived` | moves any subscription, a channel included; `active` and `suspended` swap, `archived` is final and a later `active` is refused |
| `preview --subscription … [--since <date>] [--samples N]` | judges the saved selector over its container's live posts and writes nothing -> § Preview |
| `diff --subscription … --against <filter key> [--since <date>] [--limit N] [--counts-only] [--pending]` | compares the selector with a registered filter and writes nothing -> § Diff |
| `explain --subscription … --post <id>` | judges one post's current revision: verdict, clauses, merged context, and what the log holds of it |
| `adopt --into <id\|name> --from <id>[,<id>…] [--dry-run]` | writes the posts older subscriptions hold into the selector's log -> § Adopt |

### Changing a selector

`--set` takes the selector as the wire spells it (camelCase, `contract.md` § The selector's wire), not the whole
subscription; a property the selector does not have is refused, so a typo cannot pass for a change. The usual way is to
read the stored selector with `selector`, edit it and send it back.

- The module normalizes and validates it: limits, taxonomy keys the active taxonomy holds, refs inside the container.
  A selector that parses and fails these is refused (exit 1), nothing written.
- A selector equal to the stored one keeps the version and writes nothing: `changed` is `false`.
- Otherwise the version rises and one window re-judges on the same cursor: every post the subscription has an event of,
  plus live posts changed since `--since` (default 30 days back, at most 365, at most 10,000 posts). Held posts that no
  longer match arrive as `post.unmatched` (reason `resync`), unmatched ones that match again as `post.changed`
  (`resync`), new matches as `post.new` (`backfill`).
- stdout: `{ subscriptionId, name, selectorVersion, changed, windowSinceUtc, considered, newMatches, unmatched, rematched, pending }`.
  `--by` (default `operator`) lands in the version's history row, `harvest_subscription_selectors.changed_by`.
- An archived subscription keeps its selector: the change is refused.

### Preview

Counts what the saved selector would deliver over the container's live posts that changed since `--since` (default 30
days, at most 365; at most 5,000 posts, `truncated` past it), before or after a change. It reads the subscription's own
container and tenant.

- stdout: `{ subscription, selectorVersion, sinceUtc, considered, tagged, matched, noMatch, pending, truncated, sources, clauses, samples, nearMisses }`.
  `tagged` over `considered` is the coverage; `sources` counts per source of each post's context link, so a post no
  source clause admits is in `considered` and in no source row.
- `clauses`: per subject term (`domain:…`, `topic:…`) the matched posts that stop matching without it (`adds`), per
  exclusion (`except:…`, `exclude-domain:…`, `exclude-topic:…`, `exclude-timeframe:…`, `type`) the posts that would match
  without it (`removes`).
- `samples` (default 10, 0 to 50) are the newest matches and `nearMisses` up to 10 refused posts one relaxation admits.
  Their titles are an operator review: phone numbers (9 or more digits), emails, handles and messenger links are replaced
  by `…` and a title stops at 140 characters, as in the diff.

### Diff

Judges every post of the selector's container that changed since `--since` (default 7 days back, at most 365, at most
10,000 posts) with the selector as it stands and with the named filter. `--against` takes any registered filter key
(`ocharo-fashion-v1`) or a tag filter key (`tag:concert`); an unregistered key is refused. Nothing is written.

- Each post falls in one count, in this order: `outOfScope` (no link of the post is admitted by the selector's sources,
  so neither side delivers it), `pending` (the selector cannot judge it yet), `filterUndecided` (the filter threw), then
  `both`, `filterOnly`, `selectorOnly` or `neither`; `posts` is their sum.
- stdout: `{ subscription, selectorVersion, filterKey, sinceUtc, posts, outOfScope, both, filterOnly, selectorOnly, neither, pending, filterUndecided, items }`.
- `items` list the disagreements, filter-only first, then selector-only, then the pending posts with `--pending`, each in
  post id order, together at most `--limit` (default 50, 1 to 1,000); the counts always cover every post.
  `--counts-only` lists none and excludes `--limit` and `--pending`.
- An item carries `postId`, `source` (`{family}/{handle}` of the context link), `title` and `titleEn` (scrubbed and cut to
  140 characters as in § Preview), `filter` (`yes` · `no` · `undecided`), `selector`
  (`match` · `noMatch` · `pending`), the `clauses` behind it, `entityType`, the most specific `domains`, `topics` and the
  `timeframe` of the Layer 1 answer. stderr lists the same disagreements without titles.
- A fashion-event report with `timeframe: past` that the selector's `excludeTimeframes` holds back shows as `filterOnly`
  while the filter still passes it; the `timeframe` on each item is what decides whether to keep the clause.

### Adopt

Carries the posts the older subscriptions of the same tenant and container delivered into the selector's log, so a
consumer that switches to the selector keeps what it holds and drops what the selector refuses. `--into` is an active
selector subscription; `--from` lists subscription ids (channel subscriptions, as `list` shows them) and never the
adopter.

- Considered: live posts whose newest content record in some `--from` subscription is not `post.unmatched`.
- A post the selector subscription has any record of is `alreadyHeld` and left as it is. Of the rest, the selector
  judges the current revision: refused → `post.unmatched` (reason `resync`), so the consumer drops the post it never
  learned from the selector; passes → `post.new` (`backfill`); pending → counted and given nothing until its tags arrive.
- Item ids carry `:s{version}` (`contract.md` § Event additions). `--dry-run` counts what would be written and rolls back;
  a second run writes nothing (everything it wrote is `alreadyHeld`; pending posts are counted again).
- stdout: `{ into, intoId, from, considered, alreadyHeld, unmatched, new, pending, dryRun }`.

## Provisioning and switching a consumer

Ocharo's selector is `ocharo setup … --selector` (`deployment.md` § Provisioning Ocharo on a target); `--selector-reset`
puts the shipped file back after a client's edit. A consumer's cutover to a selector subscription runs in this order:

1. Provision the selector once the sources are `auto` and tagged (§ What the selector needs); a second `ocharo setup …
   --selector` prints the same id and version.
2. `preview` it over the window the tags cover and read coverage, counts, clause counts and near misses.
3. `explain` a few posts that must match and a few that must not.
4. Run the shadow procedure for at least three days.
5. When the shadow rows are clean and the product owner gives the go: `adopt --dry-run`, then `adopt`, naming the
   subscriptions the consumer's intake sources hold now.
6. Point the consumer at the selector subscription (its intake source binds `selector:{name}`) and check its intake.
7. `state --state archived` for each older channel subscription that `list` shows; the filters retire after.

## Shadow procedure

The diff is stateless: it compares current verdicts, so nothing accumulates on the host and each run stands alone.

- Day 0: `subscriptions diff --subscription ocharo-events --tenant ocharo --against ocharo-fashion-v1 --since 2026-09-01 --limit 40 --pending`.
- Each later day: the same with `--since <yesterday> --limit 30`.
- Record each run outside this repository, since titles stay out of it: one row per run with the date, `since`, `posts`,
  `both`, `filterOnly`, `selectorOnly`, `pending` and `outOfScope`, then each listed item's `titleEn`, verdicts, clauses
  and `timeframe`.
- The cutover wants at least three daily rows and `pending` at 0 or explained, such as posts of a source still `off`.
