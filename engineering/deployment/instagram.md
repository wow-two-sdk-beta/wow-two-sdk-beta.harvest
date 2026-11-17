# Instagram test integration

Use only the test Meta account in local experiments or Wheelhouse target `harvest-dev`. Production needs a separate
account and token. Do not place tokens in source control, frontend bundles, URLs or logs.

## Connection configuration

Wheelhouse Settings/Secrets or a protected local `appsettings.Local.json` supplies only:

```ini
HarvestInstagram__AccessToken=<secret>
HarvestInstagram__ProfessionalAccountId=<querying-professional-account-id>
HarvestInstagram__ApiVersion=<supported-vN.0>
HarvestInstagram__BaseAddress=https://graph.facebook.com/
```

`AccessToken` is secret/write-only in Wheelhouse. Account ID and API version describe the connection. Never use a
Facebook Page ID in place of the professional Instagram account ID. An absent or invalid connection reports
configuration required; registration itself makes no network requests.

The previous `Enabled`, `AllowedUsernames`, `PageSize`, `MaximumPages`, `MaximumResponseBytes` and `Timeout` host
settings no longer control collection. Remove them from old target/local settings. Collection policy is persisted
through the normal SCFD registry; fixed provider transport safety bounds are code defaults.

## Profiles and limits in tables

Register one source per Instagram account in a private test container with `family=instagram`, `handle=teplofest`,
`posture=official`, `coverage=filtered`, `completeness=window`, `checkPolicy=none`, `aiAllowed=false` and
`poolShareable=false`. The registry declares the publisher name, group and account URL. Teplo Fest (`teplofest`)
and Teplo Store (`teplostore_2022`) are distinct sources under the Teplo group, using the same Instagram adapter
and Meta connection. A source's Pipelines view manages only its declared publisher profile. Its ordinary pipeline
row uses `POST /api/pipelines`, for example (replace the identifiers):

```json
{
  "containerId": "<test-container-id>",
  "sourceId": "<instagram-source-id>",
  "name": "Instagram test publisher",
  "scope": { "url": "https://www.instagram.com/teplofest/", "restrictToPath": false },
  "limits": { "pageSize": 1, "maxPages": 1 },
  "schedules": [],
  "pinned": true,
  "ai": "off"
}
```

Publisher source declarations are the approved collection inventory. A pipeline URL must match its source's
account; a source cannot collect another publisher by changing that URL. Page size and run limits use `harvest_pipelines.limits`;
source/pipeline pause, schedules and stage controls use their existing registry records. No schema migration or
host restart is needed to edit them. Keep request parameters out of `scope.parameters`: those values define scope
identity, not provider tuning. A page size change preserves the profile URL and its pipeline identity.

An operator first provisions each source through `POST /api/sources`; its registry assigns the source name and
group through the standard source update API. The console then manages that account's pipeline. The source-level
grant lets authorized tenant managers save the matching publisher pipeline and run it with the configured
Meta connection. The stored tenant field `PlatformCredentialsAllowed` is not currently enforced by the provider
runtime and does not restrict this grant. Follow-up: define and enforce a common provider-credential policy before
using that field as a tenant access boundary.

## Bounded acceptance

1. Verify the connection is ready and the test source/pipeline is unpaused. Leave schedules empty/disabled.
2. Set page size and max pages to one, then use Run, or `POST /api/runs` with the saved `pipelineId`.
3. Inspect the run and `GET /api/sources/{sourceId}/posts?limit=1`. Confirm a stored post has its publisher,
   permalink, caption, media and original publication time. A successful fixture or health check is not this test.
4. Rerun only when testing deduplication; unchanged posts should keep their existing identity/revision.
5. Pause the test pipeline when finished, or keep it available for explicit manual captures without enabling a schedule.

Expired tokens produce `LoginRequired`; renew the same host secret/grant. A rate limit records backoff and must not
trigger repeated immediate requests. Neither failure means that stored posts were deleted.

Local publisher acceptance on 2026-11-17 used the console's Capture controls for both registry declarations.
Teplo Fest completed one request/page with one unchanged post and no duplicate; its existing source, pipeline,
post and history IDs were preserved when correcting the old single-account domain source. Teplo Store completed
one request/page and saved one new post under its own source and pipeline. Both runs had zero failures, and both
pipelines retained page size one, max pages one and no schedules. The gallery showed both named sources under
Teplo, with only the source count beneath its filter pills. Credentials remained in the protected local config.

Legacy domain sources remain readable for compatibility. A domain source containing multiple accounts must not
be renamed to one publisher: provision the declared sources separately and reconcile historical provenance first.

## Local console

The deployed image currently serves the API. Run the existing console in real HTTP mode against the chosen local
API, with a separate database/ports for experiments. Use its normal API-key connection or Identity sign-in. Never
put the Meta token into the browser console connection; that token belongs only to the server's provider client.

## Integration boundary and Phase 1 acceptance

The Instagram collector ships in `v0.2.12`: provider client, SCFD broker/registry, saved profile controls, bounded
cursor paging, post/media storage and deduplication. Local acceptance captured a real professional publisher's
post and repeated the capture without creating a duplicate. DevQA runs the released collector; that deployment
does not itself prove a configured Meta connection or a scheduled Instagram run.

Each Instagram account has its own declared source and collection pipeline. Provider transport, credentials and
host pacing are shared. Collection identity and subscriber interests are independent: a consumer may request one
Instagram-wide feed across publisher sources, then receive only events matching its interests after AI analysis.
Separate subscriptions per publisher are not a product requirement. The current pull contract still follows one pipeline;
initial source-wide replay does not make it follow future captures from other pipelines. Shared delivery must
apply the subscription's declared scope consistently to both paths.

One collector-console task remains: expose Capture and its cadence in the generic pipeline view. Its current
Discover/Harvest/Process cards omit Capture; Instagram supplies neither the discovery catalog nor the Detail step
needed by the first two cards. Use the source's Pipelines > Capture now for manual collection. The engine supports
`kind=capture` schedules through ordinary pipeline updates, but the stage-settings API/UI does not expose them.
Process runs against saved captures and does not fetch Instagram posts.

The shared Ocharo work precedes further Instagram acceptance:

1. Finish the subscription scope spanning pipelines (`S09` in the [backlog](../planning/backlog.md)).
2. Connect AI extraction/relevance results to filtered consumer delivery (`S14`–`S17`). Capture currently emits
   delivery before AI, while processing results remain separate; stored results alone do not complete this flow.
3. Verify accepted, rejected and unresolved cases through Events/Desk, including repeat delivery and restart.

After that lane closes, configure the approved test connection privately on `harvest-dev`, register the chosen
professional publisher profiles and enable their selected database schedules. Verify one scheduled capture,
deduplication and the complete AI-filtered consumer flow. Keep production credentials and activation separate.
Use the existing expired-token recovery procedure; this collector does not automate Meta account onboarding or
grant renewal.

Historical backfill is deferred in the shared engine backlog. It has an admin-selected history boundary, a
three-month initial default when enabled, and no hardcoded history-duration ceiling. An external service owns
cross-feed prioritization and small background batches. Event-date extraction and recurrence belong to AI.
