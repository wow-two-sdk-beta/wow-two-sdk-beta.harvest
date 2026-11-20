# SCFD deployment

*Last updated: 2026-11-19*

One image, one host: `HarvestSources.Api` runs the capture and delivery API, the worker and the source readers, and
serves the console on the same origin. Wheelhouse deploys it as product `harvest`, service `harvest` (`deploy.yml`);
the dev SCFD runs this image.

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
  applies the migrations. The console answers at `http://127.0.0.1:8397/` and asks for the operator key.
- To upgrade a local preview, reuse its Compose project and keep the database volume.
- Two local instances: separate project names (`-p`) and env files, each with its own password, operator key and
  `HARVEST_HTTP_PORT`; start both with `HARVEST_WORKER_ENABLED=false` while configuring.
- Compose leaves CAPTCHA disabled and Instagram unconfigured; the rest comes from `.env`:

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
settings before starting the host so the SDK's `distributed_cache` set is applied; a Wheelhouse deploy's `migrate`
step does that, since it runs with the service's settings. Keep the console client secret in the vault.

`deploy.yml` routes `/` and `/api` of the `app` site to the host and declares the keys above. Its defaults:
`IsIdentityEnabled` `false`, `ClientId` `harvest-console`, `CookieName` `__Host-scfd` and `DataProtection:KeysPath`
`/var/lib/harvest/keys`, the `keys` volume. `OperatorSubjectIds:0`, `Authority` and `ClientSecret` are typed per
environment; each organization binding is an owner key `Harvest:Console:OrganizationTenants:<organization id>`, since
the descriptor cannot name an organization. Switch `IsIdentityEnabled` on only after the issuer registers the client.

On dev the issuer is Ocharo's Identity, which registers clients from `ocharo-platform`'s descriptor. The console
there is client `harvest-console`, a code-flow client with permission namespace `harvest`. Its callback and sign-out
URIs are owner keys on `ocharo-dev`.

The image serves the console from `wwwroot` (`HostConfiguration.ServeConsole`, the be sdk's SPA hosting):

- files before routing and authentication;
- `index.html` for every other unmatched route, so a deep link such as `/sources/{id}/posts/{postId}` reloads;
- a JSON `404` for an unmatched `/api` or `/.well-known` route, never the page.

Hashed `/assets` files cache for a year; documents revalidate. A host without a built console answers `404` on page
routes.

## Source profiles

`HarvestSources:Audience` picks what the host composes; each audience is its own deployment with its own PostgreSQL
database, API audience and operator credentials.

- `internal` (default; the dev SCFD): the named sources, provider settings and the Ocharo routes and verb.
- `public`: the generic engine and API only — no named source, provider setting or Ocharo route.
- A shared pool: operators register approved publishers and set section, cadence and run budgets; one collection
  pipeline per section serves any number of subscribing tenants, each with its own tenant holding
  `subscriptions:manage` and `events:read`; `filterKey: "tag:<tag>"` filters a subscription. Confirm bounded
  runs before enabling a pool's schedules.
- Sourcing is for internal use only, and every item credits its source by the external link (events-integration #50).
  Afisha stays in Ocharo's private container with pool sharing off, and a `public` target denies `afisha.uz` and
  `www.afisha.uz` in its host policy.

## Provisioning Ocharo on a target

`dotnet HarvestSources.Api.dll ocharo setup --group afisha [--model-processing <off|manual|auto>] [--selector [--selector-reset]] [--run]`
runs on the target's own settings through Wheelhouse Run, with no listener and no operator token.

- It creates or reuses tenant `ocharo`, the private container `ocharo-events`, one pipeline per harvest channel of each
  selected publisher and one `wow2.event.v1` subscription per pipeline (filter `ocharo-fashion-v1`, context
  `catalog=ocharo-fashion`). Repeating the verb keeps every existing row.
- Afisha's adapter registers Russian Fashion, Exhibitions and Concerts when the source is created. Host
  startup fills missing sections for existing sources. Only the selected Ocharo channels receive the setup cadence and
  subscription.
- Schedules stay off unless `--schedules` is given; the flag also switches on the registry cadences of existing
  pipelines (capture every 6 h, plus a daily check where the source's check policy is `web-direct`), keeping an
  interval an operator tuned.
- `--model-processing off|manual|auto` sets that model-processing mode (`engineering/development/ai-operations.md`) on
  every selected source: a new source registers with it, an existing one is updated. Without the option a new source
  registers `off` and an existing one keeps whatever mode it has, so setup never refuses a source an operator opened
  (a different sharing or robots posture still refuses). harvest-dev switches Afisha with
  `ocharo setup --group afisha --model-processing auto`; `sources processing --list` reads the result
  (`ai-operations.md` § Setting the mode from the host).
- `--selector` also provisions Ocharo's one selector subscription, `ocharo-events` (schema `wow2.event.v1`, context
  `catalog=ocharo-fashion`, live from now on), from the file the image ships, `ocharo-selector.json`: the type, domain,
  topic and timeframe clauses and the four publishers it follows (Afisha, ANONS, CAEx, Uzexpocentre; Hunarmand stays
  out while it is paused). The file names publishers by registry id, so setup binds each to the container's source group
  of that name and adds `sourceGroup` and `sourceName` to that ref's context, where the payload `context` carries
  them. Every listed publisher needs its group first, so run the groups together
  (`ocharo setup --group afisha --group anons --group caex --group uzexpocentre --selector`; CAEx's calendar is a
  selected channel the default setup lacks); a missing one refuses with `Group {id} is not provisioned in the Ocharo
  container; run setup with --group {id}.`, after the channel rows are written, so adding the group and repeating the
  verb finishes it. Repeating the verb keeps the subscription as it is, a client's edits included; `--selector-reset`
  (only with `--selector`) puts the file's selector back as a selector change by `ocharo setup`, and a file equal to the
  stored selector keeps the version. The per-pipeline `ocharo-fashion-v1` subscriptions keep being created beside it
  until they retire.
- `--run` queues one bounded capture run per pipeline for the running worker. `--refresh` queues one refresh run per
  pipeline: the worker re-reads every known post, so a changed reader emits `post.changed` for each post whose
  projection changed.
- The last stdout line is one JSON object: `tenantId`, `containerId`, `subscriptions` (`groupId`, `channelId`,
  `pipelineId`, `subscriptionId`), with `--selector` `selectorSubscriptionId` and `selectorVersion`, and `runs`
  (`pipelineId`, `kind`, `runId`); `runs` lists only runs this call queued. A consumer's `ClientGrantsJson` entry takes that `tenantId`.
- `--tenant <slug>` gives another tenant its own private container, pipelines and subscriptions;
  `ocharo setup --group afisha --tenant isolation-check` creates the empty second tenant whose subscription a
  consumer's token must not read (SCFD answers `404`).

`dotnet HarvestSources.Api.dll ocharo status [--group afisha]` writes nothing. Its last stdout line names the tenant
and, per channel, each schedule's cadence, next due time and latest run of its kind, for example
`afisha/fashion capture 6h due 11-10T19:12Z last partial/page_limit 11-10T13:12Z; check 1d due …`; it exits `1`
when setup has not run for the tenant.

The HTTP form, `POST /api/ocharo/sources/setup`, takes `{ enableSchedules, includeInstagram, groupIds?, selector,
selectorReset }` with the operator key or token: `engineering/architecture/contract.md` § Source host.

## Private source media

Set `HarvestAssets:Files:BaseAddress` and either a dedicated Files tenant key in `HarvestAssets:Files:ApiKey` or Files
Identity client settings under `HarvestAssets:Files:Identity`. The client needs `files.read` and `files.write` in the
Files tenant used for SCFD. Use a policy allowing raster images and ICO files up to 5 MiB with no orphan expiry.

The internal host queues icons for registered web sources with pipelines and copies same-host HTTPS images from
captured posts, including existing posts. Requests use Harvest's robots policy and host pacing. SCFD serves the stored
bytes through the source's authorized API and uses the source URL while an image copy is pending or unavailable.

## Provisioning Haven on a target

`dotnet HarvestSources.Api.dll marketplace setup --profile haven [--schedules] [--run] [--allow-ai] [--issue-key]` applies the
`haven` marketplace profile on the target's own settings through Wheelhouse Run, with no listener and no operator token.
A profile is data (`sources/Module/Marketplace/MarketplaceProfiles.cs`); another product adds a profile, not a verb.

- It creates or reuses tenant `haven`, the private container `haven-listings`, sources by marketplace and intake mode, and
  one pipeline per saved search. OLX reads Tashkent apartments and houses for long-term rent and Tashkent daily rent.
  Joymee reads Tashkent apartments for long-term rent. The BirBir collection pipeline starts with the apartment
  rental category and setup registers the public site source with open permission. BirBir and Domtut also accept
  authorized rental imports.
  Each pipeline gets one `wow2.post.v1` subscription
  with filter `marketplace-listing-v1` (listings only) and context `catalog=haven-rent-res`, `search=<key>`. Repeating
  the verb keeps every existing row.
- `--schedules` enables capture every 2 h for OLX, Joymee and BirBir, plus OLX's daily link check, keeping tuned intervals.
  A run reads at most 60 ads. The 64-page budget applies to catalog pages.
- `--allow-ai` opens the OLX, Joymee and BirBir collection sources to model processors (`modelProcessing` `auto`), so a
  Process stage can run `rental-terms@1`; the import sources stay closed (`off`). Setup creates collected sources closed
  and never closes an opened or `manual` one.
- `--run` queues one bounded capture per active collection pipeline. BirBir has separate direct collection and
  import sections. The BirBir and Domtut import subscriptions report `importOnly: true`.
- Haven's credential is a tenant API key named `haven-supply` with `events:read` only, sent as `X-Api-Key`.
  `--issue-key` issues it when the tenant holds no active one; `--rotate-key` revokes the active ones and issues a new
  one. The secret appears once, in the last stdout line, never on stderr; move it into Haven's target setting without
  printing it. Picked over an OAuth client: an OAuth client needs a release of the issuer's Identity (Ocharo's on dev)
  and a `ClientGrantsJson` redeploy here, and production has no SCFD issuer yet.
- The last stdout line is one JSON object: `profile`, `tenantId`, `containerId`, `searches` (`key`, `url`, `sourceId`,
  `pipelineId`, `subscriptionId`, `importOnly`, `canCapture`), `consumerKey` (`id`, `prefix`, `secret` only when issued) and `runs`.
- Haven Supply reads one subscription per search, each with its own cursor; a pull page is
  `{ data: { data, next, hasMore } }` and each listing link carries `captureId`, `externalKey`, `capturedAtUtc` and
  `sourceFields` with schema `olx.ad.v1`. A post Haven already received also reaches it as `link.state` (`suspect`,
  `gone`, `relisted`) and as merges. The searches of one site share its source, and check runs pick due links
  source-wide, so a lifecycle event can arrive on a sibling search's subscription too: apply it by `externalKey`,
  idempotently (rehearsed locally on 2026-11-14 with two direct 404s: `suspect`, then `gone`, on all three).

`dotnet HarvestSources.Api.dll marketplace status --profile haven` writes nothing; its last stdout line gives the active
key count and, per search, each schedule's cadence, next due time, latest run and the events its subscription's first
pull page holds (`events 5`, `events 100+`), so the deployed host proves delivery with no consumer key.

Through Wheelhouse Run, the job record never holds the issued secret: the runner cuts token-like runs out of the last
line and keeps that line for the owner to read once, within 15 minutes, beside the job. Issue the key when Haven's
target is ready to take it.

The HTTP form, `POST /api/marketplace/haven/setup` with `{ enableSchedules, issueKey, rotateKey }`, and
`GET /api/marketplace/profiles` / `GET /api/marketplace/haven/status` take the operator key or token.

Submit authorized BirBir or Domtut rentals to `POST /api/marketplace/haven/searches/{search}/listings` using the
operator credential. The search key is `birbir-apartments-rent` or `domtut-apartments-rent`. Send the original
`externalId`, listing `url` and `title`. The [marketplace contract](../architecture/contract.md#marketplace-sources)
lists the optional listing fields. Set Haven's corresponding subscription variable from the setup output.

## Integrations

Every provider integration ships in the image and stays inactive until configured. `HarvestIntegrations:Enabled`
and `HarvestIntegrations:Captcha:Enabled` default to `false`. Instagram requires a valid connection plus registered,
unpaused publisher pipelines; collection policy lives in tables. A configured route stays fixed for its source
origin; rotating proxy pools to get past a site's limits stay out (events-integration #50, #63).
Provider setup and protocols: [integration-providers.md](../research/integration-providers.md).

- OLX egress: enable `HarvestIntegrations:Enabled` and set `HarvestIntegrations:OlxProxyProfile` to a profile name in
  the vault setting `HarvestIntegrations:ProxyProfilesJson`, which holds that profile's proxy URI and credentials.
  The route covers `https://olx.uz` and `https://www.olx.uz`. Choose either a profile or the inline
  `HarvestIntegrations:OlxProxy:{Server,Username,Password}`; recreate the service to apply a profile change.
  [Provider comparison](../research/olx-egress-options.md)
- CAPTCHA: `deploy.yml` declares no CAPTCHA key; configuring it on a target requires declaring its credential keys.
- Instagram: `deploy.yml` declares the connection token, professional account ID, API version and Graph origin.
  Profile URLs, page/run limits, pauses and schedules are table-managed. Follow the [Instagram test procedure](instagram.md);
  the old `HarvestInstagram:Enabled` setting is no longer used.

## Wheelhouse

- `Dockerfile` builds the console in a Node stage (`pnpm --filter "@harvest/web..." build`, fe-harvest first) into the
  image's `wwwroot`, and publishes `service/HarvestSources.Api` from `engineering/codebase`. The image is
  built through shared pipelines `v0.15.0` (`publish-docker-image.yml`), whose CI runs the whole solution's tests and
  builds the release bundle. Wheelhouse v0.12+ runs migration, configuration check, rollout and verification.
- The descriptor exposes `/` and `/api` on the `app` site, and a change under `harvest.frontend-services` rebuilds the
  image. The service health probe calls `/health` over the platform network.
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

### Source stages

Each registered section has independent Discover, Harvest, and Process schedules.
Batch size bounds Harvest detail captures and Process saved captures. Discover ignores `BatchSize` and follows
the declared catalog and run budgets.
The console's Pipelines tab controls automatic scheduling and manual runs.
Choose scheduled Capture or scheduled Discover and Harvest per section. Check and Process can run with either path.

Migration `008-pipeline-stages` applies once to the shared schema. It does no source backfill.
Migration `009-step-operations` stores attempt operations for the run audit; both belong to this release.
Migration `010-post-unmatched` admits the `post.unmatched` delivery type; its rollback deletes those log rows.
Deployed hosts verify the migration level at boot. Development hosts apply pending migrations.
Roll out historical data backfills separately in indexed batches for the sources that need them.

An instance can seed missing stage settings under `Harvest:PipelineStages` in its mounted
`appsettings.Target.json`. Select a section by `PipelineId` or by its container external reference,
source handle, and section URL. Supply `Family` when a handle has both a site and an import source.
Existing stage settings take precedence over the seed on restart.

```json
{
  "Harvest": {
    "PipelineStages": [{
      "Container": "haven-listings",
      "Source": "birbir.uz",
      "Family": "Site",
      "SectionUrl": "https://birbir.uz/ru/tashkent/cat/nedvizhimost/arenda/kvartiry",
      "Schedules": [
        { "Kind": "Discover", "Mode": "Interval", "Every": "00:30:00", "Enabled": false, "Stage": {} },
        { "Kind": "Harvest", "Mode": "Interval", "Every": "00:15:00", "Enabled": false, "Stage": { "BatchSize": 30 } },
        { "Kind": "Process", "Mode": "Manual", "Enabled": false, "Stage": { "BatchSize": 20 } }
      ]
    }]
  }
}
```

Process uses saved readings. Its `Processor` defaults to `saved-reading@1`, which takes no model. For rental terms,
select `rental-terms@1`, set `Model` to a registered route and open the source to models (`marketplace setup
--allow-ai`, or `modelProcessing` on the source update; `manual` allows Process runs started by hand only). Configure `Harvest:Processing:OpenAi:Models` with model IDs and
provide `Harvest__Processing__OpenAi__ApiKey` through the environment or secret store. A model processor's results are
stored under `{processor}:{model}`; a model without a model processor is refused.
Stages can filter saved captures by title and publication time.
