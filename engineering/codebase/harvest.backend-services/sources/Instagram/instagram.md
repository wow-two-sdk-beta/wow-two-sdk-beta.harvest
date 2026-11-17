# Harvest Instagram

The core SDK `IInstagramClient` handles Meta Business Discovery requests. SCFD's `IInstagramMediaBroker` maps the
provider's results into Harvest outcomes; `InstagramStepBroker` reads the frozen pipeline scope and bounds, then
normalizes posts and advances the cursor. The generic engine owns persistence, revisions, delivery and backoff.

```csharp
services.AddHarvestInstagram().Bind(configuration.GetSection("HarvestInstagram"));
```

Only connection details belong in host configuration:

| Setting | Meaning |
|---|---|
| `AccessToken` | Approved Facebook Login token; protected host secret only |
| `ProfessionalAccountId` | The querying Page-linked professional Instagram account ID |
| `ApiVersion` | Explicit supported Graph version in `vN.0` form |
| `BaseAddress` | Guarded HTTPS Graph origin; default `https://graph.facebook.com/` |

Register family `instagram`, handle `www.instagram.com`, posture `official`, filtered/window coverage and check
policy `none`. A publisher is a persisted pipeline with `scope.url=https://www.instagram.com/USERNAME/`,
`restrictToPath=false`, `limits.pageSize` (1–50 posts, default 25) and `limits.maxPages` (default 4). Scope parameters
are not request settings and are rejected. The API validates the profile and its limits before saving. Adding or
editing a publisher needs no host restart. The console creates profiles without schedules; Run starts one capture.
Source/pipeline pauses and schedules are ordinary registry records. No host allowlist or Instagram enable flag exists.

Pipeline settings are frozen when a run starts. Targets must match that saved profile; a later settings edit affects
subsequent runs. Cursor-only pages continue without `paging.next`; an empty page ends pagination. Page/request/item,
byte and duration budgets use the generic engine. Provider transport retains fixed defensive byte/depth/time bounds.
The client performs one transport attempt; explicit authentication/rate-limit failures feed the engine's backoff.

Capture/sweep are supported; refresh, deletion checking, stories, hashtags, personal and private accounts
are not. An account's public visibility alone does not establish Business Discovery eligibility. Ocharo is an optional
consumer preset (`ocharo setup --instagram`) and does not control the generic profile registry.

Capture bodies/provenance exclude credentials, cursors, paging URLs and raw provider errors. Public links use the
canonical post permalink. Caption edits create revisions; engagement counts and rotating CDN signatures do not.
Carousel covers are not duplicated, and available video thumbnails remain usable. Publication timestamps are never
invented event dates; caption classification needs review. CDN references may expire and image text is not read.

See [test setup and acceptance](../../../../deployment/instagram.md) and
[account review](../../../../research/instagram-source-review.md).
