# deployment/

How wow-two-sdk-beta.harvest's service ships. Descriptor format: wow-two-ws/conventions/deployment/descriptor/deploy-descriptor.md.
The packages ship separately, from `publish-packages.yml` (`engineering/scripts/scripts.md`).

| File | Role |
|---|---|
| `deploy.yml` | descriptor 2: one service, `harvest` (`/health`, 256 MB, `needs: [postgres]`, the typed required key `DatabaseSettings:ConnectionString` and the secret `Harvest:Operator:ApiKey`); sites `app` (public, primary) and `console` (private), both at `/` and `/api`; `dotnetEnvironment: true` and the `verify` dev check |
| `Dockerfile` | three stages: build the frontend workspace (`fe-harvest`, then `apps/web`), publish `service/Harvest.Api` with its packages and the pages in `wwwroot`, run on `aspnet` with `curl` |
| `docker-compose.yml` | local verification: the image beside `postgres:18-alpine` with local-only credentials, on `http://127.0.0.1:8397` |

## Contract with Wheelhouse

- Image: `ghcr.io/<owner>/<repo>/harvest`, published by `.github/workflows/publish-docker-image.yml` through the shared
  `delivery.yml` of `wow-two-platform.pipelines` (pinned `v0.15.0`); CI builds the release bundle. Wheelhouse v0.12+
  runs its migrations, configuration check, rollout and verification.
- Version: `X.Y` from the newest `planning/version-track/v{X.Y}/`; the image tag and `GET /api/system/status` report the
  same `X.Y.Z`, plus the build's commit after a `+`. Package releases (`be-v*`, `fe-v*`) never move it.
- Settings a target provides in `/app/appsettings.Target.json`: `DatabaseSettings:ConnectionString` (the platform
  PostgreSQL), `Harvest:Operator:ApiKey` (`hv_` + 32 letters and digits, from the vault);
  `Deployment:TrustedProxies` or `Deployment:TrustedNetworks` names the ingress.
- Database: the platform PostgreSQL (`needs: [postgres]`), UTF-8 with `pg_trgm`. The image's `migrate` command applies
  both `harvest` and `distributed_cache`; `migrate sets --json` describes both without target settings, and
  `migrate check` provides the rollback gate. A deployed boot checks levels and store capabilities without applying
  schema changes. Local development applies migrations at boot. `GET /health` checks database readiness.
- Configuration: `dotnet Harvest.Api.dll config check` runs the boot's validation without serving. The descriptor
  declares this check before rollout. Worker, fetch and console settings reject unknown keys.
- Sessions: the image defaults `DataProtection:KeysPath` to `/data/keys`; Compose and Wheelhouse persist that directory
  in the environment's `session-keys` volume. Both Compose services use `restart: unless-stopped`.
- Dev check: after a dev rollout, `verify` curls `$VERIFY_SERVICE_HARVEST/health` and `/api/system/status` with
  `Host: localhost`; `HostFilteringTests` replays the descriptor's requests.
- After each main image publish, `conformance.yml` runs the contract suite against the released image.

## Local run

The generic service needs only PostgreSQL and the operator key. `Harvest:Fetch` configures the honest user agent,
two-second minimum host gap, timeout and response limit. `Harvest:Worker` configures leases and run limits;
`Enabled=false` disables processing without disabling the API or console. No CAPTCHA, proxy/IP, renderer, model
provider or source-specific account credentials are required. The console key is entered by the operator and held in memory.

```bash
docker build --build-arg APP_VERSION=0.1.99 --build-arg VCS_REF=$(git rev-parse HEAD) \
  -f engineering/deployment/Dockerfile -t harvest:local engineering/codebase
```

## Identity console

API-key mode remains the default. To enable browser sign-in, register a distinct confidential OIDC client for
Harvest in the chosen Identity instance. Keep its secret in host configuration or the vault. Register each exact
HTTPS callback as `{console-origin}/api/identity/callback` and its sign-out return as `{console-origin}/`.

| Setting | Value |
|---|---|
| `Harvest:Console:IsIdentityEnabled` | `true` |
| `Identity:Client:Authority` | the trusted HTTPS Identity issuer |
| `Identity:Client:ClientId` | the dedicated Harvest client ID |
| `Identity:Client:ClientSecret` | the confidential client secret; server-only |
| `Identity:Client:CookieName` | a distinct cookie, such as `__Host-harvest` |
| `Harvest:Console:PermissionNamespace` | `harvest` by default |
| `Harvest:Console:OrganizationTenants:{organization-id}` | an existing, active Harvest tenant ID |
| `DataProtection:KeysPath` | a durable, access-restricted directory shared by replicas |

The BFF persists server-side tickets in the same PostgreSQL instance using the SDK's `distributed_cache` migration
set. Every image describes both sets even while Identity is disabled, so release discovery and the target agree.
`ServiceHost` supplies the migration and configuration verbs; `MigrateHarvestHostAsync` is the local-startup helper.
Persist and protect the data-protection key directory across restarts. HTTPS is
required for secure cookies; trusted ingress configuration remains the host's responsibility.

Identity role grants are namespaced, such as `harvest:pipelines:read`, `harvest:pipelines:run`,
`harvest:pipelines:manage`, `harvest:subscriptions:manage` and `harvest:posts:read`. A tenant administrator may also
receive `harvest:containers:manage` and `harvest:sources:manage`. Never map an organization owner to Harvest's global
operator: `tenants:manage` is excluded from browser sessions. Existing integration keys remain independent.

The console reads `/api/identity/session`, echoes its CSRF token on unsafe calls and signs out through the shared
BFF. The browser receives no OIDC token or client secret. Unknown/unbound organizations see an access-denied state.
