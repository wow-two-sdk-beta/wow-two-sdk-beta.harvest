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

Register an Instagram domain source in a private test container with `family=instagram`, `handle=www.instagram.com`,
`posture=official`, `coverage=filtered`, `completeness=window`, `checkPolicy=none`, `aiAllowed=false` and
`poolShareable=false`. In the console's source Settings, add a publisher profile. The form writes an ordinary
pipeline row via `POST /api/pipelines`, for example (replace the identifiers):

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

Publisher URLs are the approved collection inventory. Page size and run limits use `harvest_pipelines.limits`;
source/pipeline pause, schedules and stage controls use their existing registry records. No schema migration or
host restart is needed to edit them. Keep request parameters out of `scope.parameters`: those values define scope
identity, not provider tuning. A page size change preserves the profile URL and its pipeline identity.

An operator first provisions the source through `POST /api/sources`; the console then manages profiles within it.
That source-level grant lets authorized tenant managers save publisher pipelines and run them with the configured
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

## Local console

The deployed image currently serves the API. Run the existing console in real HTTP mode against the chosen local
API, with a separate database/ports for experiments. Use its normal API-key connection or Identity sign-in. Never
put the Meta token into the browser console connection; that token belongs only to the server's provider client.

## Integration boundary and Phase 1 acceptance

The Instagram collector ships in `v0.2.12`: provider client, SCFD broker/registry, saved profile controls, bounded
cursor paging, post/media storage and deduplication. Local acceptance captured a real professional publisher's
post and repeated the capture without creating a duplicate. DevQA runs the released collector; that deployment
does not itself prove a configured Meta connection or a scheduled Instagram run.

Collection pipelines and subscriber interests are independent. A consumer may request one Instagram-wide feed
across publisher pipelines, then receive only events matching its interests after AI analysis. Separate
subscriptions per publisher are not a product requirement. The current pull contract still follows one pipeline;
initial source-wide replay does not make it follow future captures from other pipelines. Shared delivery must
apply the subscription's declared scope consistently to both paths.

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
