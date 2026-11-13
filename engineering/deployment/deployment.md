# SCFD deployment

*Last updated: 2026-11-12*

One image, one host: `HarvestSources.Api` runs the capture and delivery API, the worker and the source readers.
Wheelhouse deploys it as product `harvest`, service `harvest` (`deploy.yml`); the dev SCFD runs this image.

## Local Compose

From `engineering/deployment/`, copy `.env.example` to the ignored `.env` and set independent local values; generate
an operator key with `printf 'hv_%s\n' "$(openssl rand -hex 16)"`.

```sh
cd engineering/deployment
docker compose up -d --build --wait
curl --fail http://127.0.0.1:8397/health
curl --fail http://127.0.0.1:8397/api/system/status
```

- Compose project `harvest`: `database` (PostgreSQL 18, named volume `sources-database`) and `harvest` (the image,
  built from `engineering/codebase`), both `restart: unless-stopped`; the API binds loopback only. Development boot
  applies the migrations.
- To upgrade a local preview, reuse its Compose project and keep the database volume.
- Two local instances: separate project names (`-p`) and env files, each with its own password, operator key and
  `HARVEST_HTTP_PORT`; start both with `HARVEST_WORKER_ENABLED=false` while configuring.
- Compose pins CAPTCHA and Instagram off; the rest comes from `.env`:

| Variable | Default | Sets |
|---|---|---|
| `HARVEST_DB_PASSWORD` · `HARVEST_OPERATOR_KEY` | required | the database password · `Harvest:Operator:ApiKey` (`hv_` + 32 letters and digits) |
| `HARVEST_DB_NAME` · `HARVEST_DB_USER` | `harvest_sources` · `harvest` | the local database |
| `HARVEST_HTTP_PORT` | `8397` | the loopback port |
| `HARVEST_WORKER_ENABLED` | `true` | `Harvest:Worker:Enabled` |
| `HARVEST_SOURCE_AUDIENCE` | `internal` | `HarvestSources:Audience` (§ Source profiles) |
| `HARVEST_INTEGRATIONS_ENABLED` | `false` | `HarvestIntegrations:Enabled` |
| `HARVEST_OLX_PROXY_PROFILE` · `HARVEST_PROXY_PROFILES_JSON` | empty | `HarvestIntegrations:{OlxProxyProfile,ProxyProfilesJson}` |
| `HARVEST_OLX_PROXY_{SERVER,USERNAME,PASSWORD}` | empty | `HarvestIntegrations:OlxProxy:*`, the inline alternative to a profile |

## Shared API access

Register one SCFD resource, named `harvest`, in the environment's Identity issuer. On dev that issuer is Ocharo's
Identity; a separate SCFD issuer comes later. The resource's HTTPS URI, including the trailing slash, is the API
audience. Register separate confidential `client_credentials` clients for the operator and each
consumer. Clients request a token with that URI as `resource` and send it as `Authorization: Bearer`.

| Setting | Shared target value |
|---|---|
| `Harvest:ServiceAccess:Enabled` | `true` |
| `Harvest:ServiceAccess:Authority` | HTTPS issuer from Identity discovery |
| `Harvest:ServiceAccess:Audience` | Exact registered resource URI |
| `Harvest:ServiceAccess:ClientGrantsJson` | JSON mapping client IDs to operator or tenant grants |

```json
{
  "harvest-operator": { "isOperator": true, "scopes": ["tenants:manage", "sources:manage"] },
  "haven-supply": { "tenantId": "00000000-0000-0000-0000-000000000001", "scopes": ["events:read"] }
}
```

Replace the sample tenant ID with an active SCFD tenant; for Ocharo's subscriptions it is the `tenantId` that
`ocharo setup` prints. The operator gets only the administration scopes it uses. Each registry scope `{noun}:{verb}`
is registered in Identity as `harvest.{noun}-{verb}`: `harvest.tenants-manage`, `harvest.containers-manage`,
`harvest.sources-manage`, `harvest.pipelines-read`, `harvest.pipelines-run`, `harvest.pipelines-manage`,
`harvest.subscriptions-manage`, `harvest.events-read`, `harvest.links-submit`, `harvest.entries-write` and
`harvest.posts-read`. The grant mapping keeps the registry names. SCFD intersects the token's scopes with the grant's
and applies tenant ownership at each endpoint. Consumer credentials belong in each consumer's backend vault. SCFD
needs only the issuer, audience and grant mapping.

## Browser sign-in

Enable `Harvest:Console:IsIdentityEnabled` and serve the console and its `/api` over the same HTTPS origin.
Register a confidential authorization-code client with Identity. Use `{console-origin}/api/identity/callback`
as its callback URI and `{console-origin}/` as its sign-out return URI.

| Setting | Value |
|---|---|
| `Identity:Client:Authority` | The HTTPS Identity issuer |
| `Identity:Client:ClientId` and `Identity:Client:ClientSecret` | The console's server-side client credentials |
| `Identity:Client:CookieName` | A distinct cookie name, such as `__Host-scfd` |
| `Harvest:Console:OperatorSubjectIds` | Exact subject IDs (`sub`) of approved SCFD platform admins |
| `Harvest:Console:OrganizationTenants:{organization-id}` | The organization's active SCFD tenant ID |
| `Harvest:Console:PermissionNamespace` | The Identity client's permission namespace, `harvest` by default |
| `DataProtection:KeysPath` | A persistent, access-restricted key directory shared by replicas |

Grant platform admin access by adding the person's Identity subject to `OperatorSubjectIds`. Platform admins
manage tenants and stored captures and posts. For a client account, bind its active Identity organization to
the SCFD tenant and grant the role the relevant permissions, such as `harvest:sources:manage` and
`harvest:pipelines:run`. Clients can also read and subscribe to operator-managed pools with the corresponding
permissions. Keep browser sign-in and service clients on separate Identity registrations.

Local boot applies the session cache migration. For a deployed host, run `migrate` with the browser sign-in
settings before starting the host so the SDK's `distributed_cache` set is applied. Keep the console client secret
in host settings or the vault. `deploy.yml` routes only `/api` today, so no target serves the console yet.

## Source profiles

`HarvestSources:Audience` picks what the host composes; each audience is its own deployment with its own PostgreSQL
database, API audience and operator credentials.

- `internal` (default; the dev SCFD): the named sources, provider settings and the Ocharo routes and verb.
- `public`: the generic engine and API only — no named source, provider setting or Ocharo route.
- A shared pool: operators register approved publishers and set section, cadence and run budgets; one collection
  pipeline per section serves any number of subscribing tenants, each with its own tenant holding
  `subscriptions:manage` and `events:read`; `filterKey: "tag:<tag>"` filters a subscription. Confirm allowed use and
  bounded runs before enabling a pool's schedules.
- Nothing sourced is republished (events-integration #50). Afisha stays in Ocharo's private container with pool
  sharing off, and a `public` target denies `afisha.uz` and `www.afisha.uz` in its host policy. Afisha's terms ask
  written permission to reuse its materials; the private `ocharo-fashion` catalog reuses none
  ([terms](https://www.afisha.uz/ru/terms)).

## Provisioning Ocharo on a target

`dotnet HarvestSources.Api.dll ocharo setup --group afisha [--run]` runs on the target's own settings through
Wheelhouse Run, with no listener and no operator token.

- It creates or reuses tenant `ocharo`, the private container `ocharo-events`, one pipeline per harvest channel of each
  selected publisher and one `wow2.event.v1` subscription per pipeline (filter `ocharo-fashion-v1`, context
  `catalog=ocharo-fashion`). Repeating the verb keeps every existing row.
- Schedules stay off unless `--schedules` is given; the flag also switches on the registry cadences of existing
  pipelines (capture every 6 h, plus a daily check where the source's check policy is `web-direct`), keeping an
  interval an operator tuned.
- `--run` queues one bounded capture run per pipeline for the running worker. `--refresh` queues one refresh run per
  pipeline: the worker re-reads every known post, so a changed reader emits `post.changed` for each post whose
  projection changed.
- The last stdout line is one JSON object: `tenantId`, `containerId`, `subscriptions` (`groupId`, `channelId`,
  `pipelineId`, `subscriptionId`) and `runs` (`pipelineId`, `kind`, `runId`); `runs` lists only runs this call
  queued. A consumer's `ClientGrantsJson` entry takes that `tenantId`.
- `--tenant <slug>` gives another tenant its own private container, pipelines and subscriptions;
  `ocharo setup --group afisha --tenant isolation-check` creates the empty second tenant whose subscription a
  consumer's token must not read (SCFD answers `404`).

`dotnet HarvestSources.Api.dll ocharo status [--group afisha]` writes nothing. Its last stdout line names the tenant
and, per channel, each schedule's cadence, next due time and latest run of its kind, for example
`afisha/fashion capture 6h due 11-10T19:12Z last partial/page_limit 11-10T13:12Z; check 1d due …`; it exits `1`
when setup has not run for the tenant.

The HTTP form, `POST /api/ocharo/sources/setup`, takes `{ enableSchedules, includeInstagram, groupIds? }` with the
operator key or token: `engineering/architecture/contract.md` § Source host.

## Provisioning Haven on a target

`dotnet HarvestSources.Api.dll marketplace setup --profile haven [--schedules] [--run] [--issue-key]` applies the
`haven` marketplace profile on the target's own settings through Wheelhouse Run, with no listener and no operator token.
A profile is data (`sources/Module/Marketplace/MarketplaceProfiles.cs`); another product adds a profile, not a verb.

- It creates or reuses tenant `haven`, the private container `haven-listings`, one OLX source (`www.olx.uz`, private,
  AI off, robots obeyed, `web-direct` checks) and one pipeline per saved search: Tashkent apartments and houses for
  long-term rent and Tashkent daily rent, each read newest first. Each pipeline gets one `wow2.post.v1` subscription
  with filter `marketplace-listing-v1` (listings only) and context `catalog=haven-rent-res`, `search=<key>`. Repeating
  the verb keeps every existing row.
- Schedules stay off unless `--schedules` is given: capture every 2 h and a daily link check, keeping an interval an
  operator tuned. A run reads at most 60 ads (`maxItems`); its page budget, 64, counts each ad's detail page too.
- `--run` queues one bounded capture run per search for the running worker.
- Haven's credential is a tenant API key named `haven-supply` with `events:read` only, sent as `X-Api-Key`.
  `--issue-key` issues it when the tenant holds no active one; `--rotate-key` revokes the active ones and issues a new
  one. The secret appears once, in the last stdout line, never on stderr; move it into Haven's target setting without
  printing it. Picked over an OAuth client: an OAuth client needs a release of the issuer's Identity (Ocharo's on dev)
  and a `ClientGrantsJson` redeploy here, and production has no SCFD issuer yet.
- The last stdout line is one JSON object: `profile`, `tenantId`, `containerId`, `sourceId`, `searches` (`key`, `url`,
  `pipelineId`, `subscriptionId`), `consumerKey` (`id`, `prefix`, `secret` only when issued) and `runs`.
- Haven Supply reads one subscription per search, each with its own cursor; a pull page is
  `{ data: { data, next, hasMore } }` and each listing link carries `captureId`, `externalKey`, `capturedAtUtc` and
  `sourceFields` with schema `olx.ad.v1`. A post Haven already received also reaches it as `link.state` (`suspect`,
  `gone`, `relisted`) and as merges.

`dotnet HarvestSources.Api.dll marketplace status --profile haven` writes nothing; its last stdout line gives the active
key count and, per search, each schedule's cadence, next due time and latest run.

The HTTP form, `POST /api/marketplace/haven/setup` with `{ enableSchedules, issueKey, rotateKey }`, and
`GET /api/marketplace/profiles` / `GET /api/marketplace/haven/status` take the operator key or token.

## Integrations

Every provider integration ships in the image and stays off until explicitly configured: `HarvestIntegrations:Enabled`,
`HarvestIntegrations:Captcha:Enabled` and `HarvestInstagram:Enabled` default to `false`. A configured route stays
fixed for its source origin; rotating proxy pools to get past a site's limits stay out (events-integration #50, #63).
Provider setup and protocols: [integration-providers.md](../research/integration-providers.md).

- OLX egress: enable `HarvestIntegrations:Enabled` and set `HarvestIntegrations:OlxProxyProfile` to a profile name in
  the vault setting `HarvestIntegrations:ProxyProfilesJson`, which holds that profile's proxy URI and credentials.
  The route covers `https://olx.uz` and `https://www.olx.uz`. Choose either a profile or the inline
  `HarvestIntegrations:OlxProxy:{Server,Username,Password}`; recreate the service to apply a profile change.
  [Provider comparison](../research/olx-egress-options.md)
- CAPTCHA and Instagram: `deploy.yml` declares no CAPTCHA key and only `HarvestInstagram:Enabled`; enabling either on
  a target adds its credential keys there first. Instagram's settings: `codebase/harvest.backend-services/sources/Instagram/instagram.md`.

## Wheelhouse

- `Dockerfile` publishes `service/HarvestSources.Api` from `engineering/codebase`; nothing is downloaded. The image is
  built through shared pipelines `v0.15.0` (`publish-docker-image.yml`), whose CI runs the whole solution's tests and
  builds the release bundle. Wheelhouse v0.12+ runs migration, configuration check, rollout and verification.
- The descriptor exposes `/api` on the `app` site. The service health probe calls `/health` over the platform network.
- Target settings in `/app/appsettings.Target.json` include `DatabaseSettings:ConnectionString`, the service access
  settings above, `HarvestSources:Audience`, the integration keys and ingress trust via `Deployment:TrustedProxies` or
  `Deployment:TrustedNetworks`.
- PostgreSQL requires UTF-8 and `pg_trgm`. `dotnet HarvestSources.Api.dll migrate sets --json` lists the `harvest`
  set; `migrate check` checks rollback eligibility. Local boot applies migrations; deployed boot checks the schema
  level.
- `dotnet HarvestSources.Api.dll config check` validates target settings, service access and source options included.
  The dev probe checks `/health` and `/api/system/status` with `Host: localhost`; the status response reports the image
  version and source revision.
- Moving the dev SCFD from the former `harvestadapters` product to `harvest` keeps its database and Events'
  subscription IDs (events-integration #63). Provision subscriptions before enabling consumer jobs, and confirm bounded runs
  and tenant isolation on dev before enabling schedules.
