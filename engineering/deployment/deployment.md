# SCFD deployment

*Last updated: 2026-11-21*

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
  pipelines (capture on the cron `0 1,7,13,19 * * *` in `Asia/Tashkent`, four windows a day, plus a 24 h check where
  the source's check policy is `web-direct`; Instagram capture stays off), keeping the rows a pipeline holds, such as
  a tuned interval or the old fixed 6 h capture, and adding the ones it lacks. Every profile takes that capture
  cadence, the generic routes (`generic-feed`, `generic-structured`, `generic-site`) and `telegram-preview` included.
- `--model-processing off|manual|auto` sets that model-processing mode (`engineering/development/ai-operations.md`) on
  every selected source: a new source registers with it, an existing one is updated. Without the option a new source
  registers `off` and an existing one keeps whatever mode it has, so setup never refuses a source an operator opened
  (a different sharing or robots posture still refuses). harvest-dev switches Afisha with
  `ocharo setup --group afisha --model-processing auto`; `sources processing --list` reads the result
  (`ai-operations.md` § Setting the mode from the host).
- `--selector` also provisions Ocharo's one selector subscription, `ocharo-events` (schema `wow2.event.v1`, context
  `catalog=ocharo-fashion`, live from now on), from the file the image ships, `ocharo-selector.json`: the type, domain,
  topic and timeframe clauses and `followRegistry: true`: the selector follows every registry publisher the container
  provisions, in registry rank order, so a publisher joins by being provisioned and needs no file edit. The file's four
  `sources` entries (Afisha, ANONS, CAEx, Uzexpocentre) only give per-publisher context, such as the `defaultKind` of
  CAEx and Uzexpocentre; a listed publisher that is not provisioned is skipped. Setup binds each provisioned publisher
  to the container's source group of that name and adds `sourceGroup` and `sourceName` to that ref's context, where
  the payload `context` carries them. Name the publishers Ocharo should see
  (`ocharo setup --group afisha --group anons --group caex --group uzexpocentre --selector`; CAEx's calendar is a
  selected channel the default setup lacks, and Hunarmand stays out while it is paused, so leave its group out); a
  container with no registry publisher refuses with `No registry publisher is provisioned in the Ocharo container; run
  setup.`, after the channel rows are written. A file without `followRegistry` lists the publishers it follows, each
  needs its group first, and a missing one refuses with `Group {id} is not provisioned in the Ocharo container; run
  setup with --group {id}.`. Repeating the verb keeps the subscription as it is, a client's edits included;
  `--selector-reset` (only with `--selector`) puts the file's selector back as a selector change by `ocharo setup`, and
  a file equal to the stored selector keeps the version. The per-pipeline `ocharo-fashion-v1` subscriptions keep being
  created beside it until they retire.
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
`afisha/fashion capture cron due 11-11T02:00Z last partial/page_limit 11-10T20:03Z; check 1d due …`; it exits `1`
when setup has not run for the tenant.

The HTTP form, `POST /api/ocharo/sources/setup`, takes `{ enableSchedules, includeInstagram, groupIds?, selector,
selectorReset }` with the operator key or token: `engineering/architecture/contract.md` § Source host.

## Private source media

Set `HarvestAssets:Files:BaseAddress` and either a dedicated Files tenant key in `HarvestAssets:Files:ApiKey` or Files
Identity client settings under `HarvestAssets:Files:Identity`. The client needs `files.read` and `files.write` in the
Files tenant used for SCFD. Use a policy allowing raster images and ICO files up to 5 MiB with no orphan expiry.

The internal host queues icons for registered web sources with pipelines and copies HTTPS images from captured posts,
including existing posts: those on the source's own host, and those on the image domains its adapter declares
(`AdapterDescriptor.Media`; OLX declares `olxcdn.com`, first 6 images of a post), a declared domain and its subdomains
only, with the default port and no credentials in the URL. Requests use Harvest's robots policy and host pacing, and a
declared domain's images keep the adapter's gap per image host. SCFD serves the stored bytes through the source's
authorized API and uses the source URL while an image copy is pending or unavailable.

The copier records each attempt on the job (migration `020-asset-outcomes`): `outcome` and `finished_at` for every attempt,
and `bytes`, the bytes uploaded to Files, for a copy. The outcome is one of `copied`; `rejected` (the URL does not parse or
the destination policy refuses it); `fetch_<outcome>` (the fetch did not return `Ok`: `fetch_gone`,
`fetch_robots_disallowed`, `fetch_rate_limited` and so on); `redirected_off_policy` (the final URL fails the policy);
`unsupported_type`, `empty` and `too_large` (the content checks); and `files_declined` (Files refused the upload; the job
retries in 15 minutes). A copy made before the migration keeps no size or outcome: it reads as unsized and `unrecorded`.

`dotnet HarvestSources.Api.dll assets report [--sources <id,id>]` reads those records through Wheelhouse Run, with no
database access, listener or operator token. It writes nothing, prints one JSON line and exits `0` (`1` the store could not be
read, `2` usage). It prints counts only, never an origin URL, file id, contact value or post text; an error names the failing
argument. The line holds:

- `generatedAt`, `sources` and `totals`; `--sources` limits the output to the named sources. A source is listed when it is
  active and has any asset job or any linked post.
- per source `sourceId`, `handle` and `family`, then `images`: `jobs`, `copied`, `due` (not copied, retry time reached),
  `deferred` (not copied, retry later) and `byOutcome` (a count per outcome, `unrecorded` for a job with none);
  `bytes` over the copied images: `sized`, `unsized` and, over the sized ones, `total`, `min`, `median` (the lower middle
  value of an even count) and `max`, all `null` when none is sized; `favicon.copied` (`true`, `false`, or `null` with no
  job); and `posts` over linked posts that are live (not merged, not `gone`, link not retired): `total`, `withContact`,
  `withPhone` and `withEmail`, read from the `kind` of each item of the post's `contacts`.
- `totals`: `sources`, `images`, `bytes` and `posts` over the listed sources, a post linked to several of them counted once.

A result line over 16 KiB is cut by Wheelhouse; the verb says so on stderr, and `--sources` names fewer sources.

## Provisioning Haven on a target

`dotnet HarvestSources.Api.dll marketplace setup --profile haven [--schedules] [--run] [--model-processing off|manual|auto] [--issue-key]` applies the
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
- `--model-processing off|manual|auto` sets that mode on the OLX, Joymee and BirBir collection sources, as `ocharo setup`
  does: new sources register with it and existing ones are updated; without it new sources register `off` and existing
  ones keep their mode. The import sources stay `off`. `auto` lets the AI passes run on every write, `manual` only on a
  named run (`extract run --post`, `processing`), `off` never (`ai-operations.md`). `--allow-ai` still works as the older
  form: it opens a collected source that is `off` to `auto` and never touches a `manual` or `auto` one; it cannot be
  combined with `--model-processing`. The HTTP form takes `allowAi` and `modelProcessing` the same way.
- AI results for Haven (`development/extraction.md`, `architecture/contract.md` § Listing delivery): set the host's
  `Harvest__Processing__Ai__Enabled`, the provider key, `Harvest__Processing__Tags__Enabled` and Haven's wanted pair beside
  Ocharo's, `Harvest__Processing__Extraction__Wanted__1__EntityType` `listing` and `Harvest__Processing__Extraction__Wanted__1__Domain`
  `real-estate` (`deploy.yml` declares `:1`; `:0` is Ocharo's `event` + `style-fashion`), register the listing prompt with
  `extract prompt --use-case listing --file …`, then run `marketplace setup --profile haven --model-processing auto`. The three
  OLX searches, Joymee's and BirBir's collection skip `post-tags`: OLX's sections imply `listing` and `real-estate/apartments`,
  `real-estate/houses` and `real-estate/real-estate-renting-and-leasing`, Joymee's and BirBir's apartment sections `listing` and
  `real-estate/apartments`, which the write stores in code (tier `Rules`), and `extract-listing` queues on
  the write, on Haiku by the default `Extraction:Models:0`. Each done
  answer reaches the search's subscription as `post.extracted` with `base.extras.listingAi`; a failed one reaches nothing. Haven's
  key keeps `events:read`. An OLX ad stored before this takes its section tags on its next write (a changed capture, a
  state change) or when `post-tags backfill` queues it, which tags it in code, not with a model call; that raises the post's
  revision, but Haven's subscription is sent no `post.changed` for a revision that only moved the tags (the
  `marketplace-listing-v1` filter withholds it), while a changed title, text, price or state is; `extract backfill` then
  queues what has tags and no answer.
- AI spend: `Harvest__Processing__Budget__LimitUsd` (with `SoftShare`, `CycleStartDay`, `SinceUtc`; `deploy.yml` declares
  them) caps the AI cost of this target so one credit is not overrun; `processing budget` reads it
  (`development/ai-operations.md` § Spend guard).
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

The HTTP form, `POST /api/marketplace/haven/setup` with `{ enableSchedules, issueKey, rotateKey, allowAi, modelProcessing }`, and
`GET /api/marketplace/profiles` / `GET /api/marketplace/haven/status` take the operator key or token.

Submit authorized BirBir or Domtut rentals to `POST /api/marketplace/haven/searches/{search}/listings` using the
operator credential. The search key is `birbir-apartments-rent` or `domtut-apartments-rent`. Send the original
`externalId`, listing `url` and `title`. The [marketplace contract](../architecture/contract.md#marketplace-sources)
lists the optional listing fields. Set Haven's corresponding subscription variable from the setup output.

Haven's OLX searches on harvest-prod run at a 15 s host pace. That host has no operator route, so the `pipelines` host verb
does the operator's pace, run and schedule steps through Wheelhouse Run with no operator token. Each form prints one JSON
object as its last stdout line and exits `0` done, `1` refused or not found, `2` usage; the pipeline ids are the setup
output's `searches[].pipelineId`.

- `pipelines pace --source site/www.olx.uz --tenant haven --ms 15000` sets the least gap between two requests to the source's
  host, 1000–60000 ms. Every source on that host shares it, so the pace is the host's, not Haven's alone.
- `pipelines run --pipeline <id> [--kind capture|check|refresh|discover|harvest]` queues one run (capture by default) and is
  refused while a run of that kind is queued or running. `sweep` is a usage error.
- `pipelines schedule --pipeline <id> --kind capture|check|refresh|discover|harvest --every 2h [--off] [--walk full|new|resume]
  [--batch <1-500>] [--gap-ms <0-60000|host>] [--parallel <1-16>]` sets that kind's interval (`15s`, `15m`, `1h`, `2h30m` or
  `01:00:00`; at least 5 minutes, and for `harvest` at least 15 seconds), disabled with `--off`; the other schedules, the name,
  the scope and the limits stay. A `discover` or `harvest` row is a stage row, saved as `PUT api/pipelines/{id}/stages` saves
  it; enabling one while the pipeline has an enabled capture schedule is refused with exit `1`, and the message names the
  command to run first (`pipelines schedule --pipeline <id> --kind capture --every <d> --off`). Turning a row on that was off
  clears the pipeline's automatic turn-off and starts its failed-run count again, and the report line says so.
  - The stage flags tune the stage row, and an omitted flag keeps the stored value: `--walk` (discover only) is `full` to read
    the whole catalog on every run, `new` to read from the first page until a page saves nothing new, or `resume` (the
    default) to continue at the cursor; `--batch` (harvest only) is the items a run takes, 1–500; `--parallel` (harvest only)
    is how many it fetches at once, 1–16; `--gap-ms` (discover and harvest) is the gap between the starts of the stage's own
    requests to its host, 0–60000 ms, and `host` clears it so the host's pace applies again. A flag on another kind, a
    repeated flag or a value that is not a number is a usage error (exit `2`); a number out of range is refused with exit `1`
    and nothing saved. Each stage row (discover, harvest, process) prints a `stage` object
    `{"batch","walk","gapMs","parallel","disabled"}` in this form and in `show`, with `"resume"`, `"host"` and `1` for the
    unset values.
  - Only an operator sets `--gap-ms` and `--parallel`, as only an operator sets the host pace: the verb runs as the host's
    operator, and a tenant key that sends `gapMs` or `parallel` to `PUT api/pipelines/{id}/stages` (or sets them on a pipeline
    it declares or updates) gets 403 and nothing is saved. A tenant save that keeps the stored values passes.
  - A stage's own gap replaces the host's pace (`pipelines pace`), the reader's declared gap and the 2 s fetch minimum for
    that stage's requests, so a host pace under 2000 ms still behaves as 2000 ms for every request that has no gap of its own;
    `--gap-ms` is the way to go under it. What still binds a request that has its own gap: the host's `robots.txt`
    `Crawl-delay`, a `Retry-After`, a pause after a challenge, block or rate limit (for every pipeline on the host), and a
    delay a refusal raised above the minimum. A refusal stops the run and pauses the host, and a reply still in flight does not
    lift that pause.
- `pipelines auto-off --pipeline <id> --after <0-1000|default>` sets how many failed runs in a row turn this pipeline's
  schedules off: `0` never, `default` the host's value. It writes only that value; it never turns a row on or off and never
  starts the count again. Exit `2` for a missing or out-of-range `--after` or any other flag.
- `pipelines show --pipeline <id> [--runs <n>]` writes nothing; it reads the pipeline's name, state, limits and schedules, the
  discover and harvest stage rows among them, its `autoOff` (`after`, `fromDefault`, the `failures` in a row so far and `off`,
  null or the turn-off's `atUtc`, `runId`, `failures` and `reason`), and its newest runs' counters, with no post text and no URL.

A capture run restarts at catalog page 1 and re-reads the newest ads, so a source with a large catalog (Joymee on harvest-prod)
is walked with the discover and harvest pair instead: discover lists the catalog's pages into the saved discoveries (from its
cursor, or from the first page with `--walk full` or `--walk new`), and harvest captures the pending ones in batches (§ Source
stages). Capture off, the whole catalog listed every 5 minutes, 50 ads harvested every 15 seconds, 250 ms apart, 4 at once:

```
pipelines schedule --pipeline <id> --kind capture --every 6h --off
pipelines schedule --pipeline <id> --kind discover --every 5m --walk full
pipelines schedule --pipeline <id> --kind harvest --every 15s --batch 50 --gap-ms 250 --parallel 4
```

Add `--gap-ms` to the discover line when the walk should not slow the harvest. A walk that takes longer than its interval runs
back to back: a slot that finds the pipeline's discover run still active queues nothing and passes, so the next run starts at
the first slot after the walk ends. `--walk new` reads only the newest pages (it stops at the first page that holds nothing
new), which costs about a page or two per run when little is new.

`pipelines run --pipeline <id> --kind discover` and `--kind harvest` start either stage at once, before its first due time.

A pipeline stays as it is while its runs fail: each blocked run is data, so nothing stops a source that refuses us. Only a long
streak does, and only by itself. When 10 capture, check, refresh, discover or harvest runs in a row have failed, the pipeline's
schedules of those five kinds turn off (`enabled` false, no next due time) and the turn-off is logged and recorded with the time,
the run that reached the count and what it ended on (`pipelines show`'s `autoOff`; the console's Pipelines pages). A run fails
when it saved nothing and ended `failed`, or `partial` after a failed step: a block, a challenge, a waiting room, a rate limit,
a login or robots refusal, a network error, a limit reached with every step failing, or a queue-time failure. A run that
captured anything, `partial` included, a discover run that saved discoveries and any other `completed` run start the count
again; a `completed` `no_items` run, a cancelled run and a run with nothing saved and no failed step (a page limit) neither
count nor reset. Process schedules, the pipeline's state and runs started by hand stay as they are. To turn a row back on, use
`pipelines schedule … --every <d>`, the console, `PUT api/pipelines/{id}` or `PUT api/pipelines/{id}/stages`: it clears the record
and starts the count again, and runs queued before it never count. `ocharo setup` and `marketplace setup` with `--schedules`,
and the startup stage seeds, leave a turned-off pipeline as it is.

The host's value is `Harvest:Worker:AutoOffAfter` (`Harvest__Worker__AutoOffAfter`, 0–1000, default `10`; `0` never turns any
pipeline off), and a pipeline's own `pipelines auto-off` value wins. Runs from before the release never count, so a pipeline on
harvest-prod that is blocked on every run turns off about 2.7 days after the release (ten refused hourly captures, each waiting
out its lane's pause) unless it is given `pipelines auto-off --pipeline <id> --after 0`.

`assets report [--sources <id,id>]` is the read-only form for a source's image copies: copies by state, outcome and bytes, and
live posts by contact kind, as counts only (§ Private source media).

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
Batch size bounds Harvest detail captures (1–500) and Process saved captures (1–2000). Discover ignores `BatchSize` and follows
the declared catalog and run budgets.

- **Walk** (`Walk` on a Discover stage): `Resume`, the default, continues at the saved cursor and keeps the pipeline's request
  budget. `Full` clears the cursor and lists the whole catalog from its first page on every run, saving only the ads it does
  not hold yet (a known ad stays captured); `New` clears the cursor too but stops paging at the first catalog page that saves
  no new ad, so a newest-first catalog costs about a page or two per run when little is new. A `Full` or `New` walk is not cut
  by the pipeline's request budget: a run of it ends `exhausted` at the catalog's last page (or `New`'s first known page), and
  only its byte and time budgets (and the engine's own request ceiling) still end it early.
- **Harvest budget**: a run takes up to its `BatchSize` pending items and its request budget does not cut it, so a batch of
  100 under a pipeline `MaxRequests` of 70 captures 100. A scheduled Harvest slot with nothing pending queues no run (the next
  due time still moves on), which keeps a 15-second schedule from filling the run history; a run started by hand with nothing
  pending still records `no_items`.
- **Interval**: a Harvest schedule may run as often as every 15 seconds (`00:00:15`); every other schedule stays at 5 minutes or
  more.
- **Gap and parallel fetches** (`GapMs` 0–60000 ms and `Parallel` 1–16 on a Harvest stage; `GapMs` also on Discover): the
  gap between the starts of the stage's own requests to its host replaces the host pace, the reader's gap and the 2 s fetch
  minimum, and a Harvest stage with `Parallel` above 1 fetches that many items at once. The host's `Crawl-delay`, `Retry-After`,
  pauses and refusal delays still bind, and only an operator may set either (a tenant key gets 403). A run that sets neither
  paces and claims as before.
- **Claim pools**: `Harvest:Worker:ParallelSlots` (default 16, 1–64) is how many claims of parallel runs a worker executes at
  once, beside its one slot for every other run; a parallel run never holds more items than its `Parallel`.
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
Existing stage settings take precedence over the seed on restart. `Walk` is `Resume`, `Full` or `New`.

```json
{
  "Harvest": {
    "PipelineStages": [{
      "Container": "haven-listings",
      "Source": "birbir.uz",
      "Family": "Site",
      "SectionUrl": "https://birbir.uz/ru/tashkent/cat/nedvizhimost/arenda/kvartiry",
      "Schedules": [
        { "Kind": "Discover", "Mode": "Interval", "Every": "00:05:00", "Enabled": false, "Stage": { "Walk": "Full" } },
        { "Kind": "Harvest", "Mode": "Interval", "Every": "00:00:15", "Enabled": false, "Stage": { "BatchSize": 50, "GapMs": 250, "Parallel": 4 } },
        { "Kind": "Process", "Mode": "Manual", "Enabled": false, "Stage": { "BatchSize": 20 } }
      ]
    }]
  }
}
```

Process uses saved readings. Its `Processor` defaults to `saved-reading@1`, which takes no model. For rental terms,
select `rental-terms@1`, set `Model` to a registered route and open the source to models (`marketplace setup
--model-processing auto`, or `modelProcessing` on the source update; `manual` allows Process runs started by hand only). Configure `Harvest:Processing:OpenAi:Models` with model IDs and
provide `Harvest__Processing__OpenAi__ApiKey` through the environment or secret store. A model processor's results are
stored under `{processor}:{model}`; a model without a model processor is refused.
Stages can filter saved captures by title and publication time.
