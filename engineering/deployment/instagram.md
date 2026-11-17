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
