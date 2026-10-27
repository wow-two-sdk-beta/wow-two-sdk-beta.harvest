# deployment/

How wow-two-sdk-beta.harvest's service ships. Descriptor format: wow-two-ws/conventions/deployment/descriptor/deploy-descriptor.md.
The packages ship separately, from `publish-packages.yml` (`engineering/scripts/scripts.md`).

| File | Role |
|---|---|
| `deploy.yml` | descriptor 2: one service, `harvest` (`/health`, 256 MB, `needs: [postgres]`, the typed required key `DatabaseSettings:ConnectionString` and the secret `Harvest:Operator:ApiKey`); sites `app` (public, primary) and `console` (private), both at `/` and `/api`; `dotnetEnvironment: true` and the `verify` dev check |
| `Dockerfile` | three stages: build the frontend workspace (`fe-harvest`, then `apps/web`), publish `service/Harvest.Api` with its packages and the pages in `wwwroot`, run on `aspnet` with `curl` |
| `docker-compose.yml` | local verification: the image beside `postgres:16-alpine` with local-only credentials, on `http://127.0.0.1:8397` |

## Contract with Wheelhouse

- Image: `ghcr.io/<owner>/<repo>/harvest`, published by `.github/workflows/publish-docker-image.yml` through the shared
  `delivery.yml` of `wow-two-platform.pipelines` (pinned `v0.10.0`); CI never deploys. `v0.10.0` carries the typed
  setting keys (`settings.keys`) into the bundle; the runner needs Wheelhouse v0.5 or later.
- Version: `X.Y` from the newest `planning/version-track/v{X.Y}/`; the image tag and `GET /api/system/status` report the
  same `X.Y.Z`, plus the build's commit after a `+`. Package releases (`be-v*`, `fe-v*`) never move it.
- Settings a target provides in `/app/appsettings.Target.json`: `DatabaseSettings:ConnectionString` (the platform
  PostgreSQL), `Harvest:Operator:ApiKey` (`hv_` + 32 letters and digits, from the vault);
  `Deployment:TrustedProxies` or `Deployment:TrustedNetworks` names the ingress.
- Database: the platform PostgreSQL (`needs: [postgres]`), UTF-8 with `pg_trgm`. Before serving, the host creates the
  database if missing, refuses one that is not UTF-8, lacks `pg_trgm` or whose trigrams skip Cyrillic, and applies the
  migration set `harvest`. `GET /health` answers 200 only while the database answers.
- Dev check: after a dev rollout, `verify` curls `$VERIFY_SERVICE_HARVEST/health` and `/api/system/status` with
  `Host: localhost`; `HostFilteringTests` replays the descriptor's requests.
- After each main image publish, `conformance.yml` runs the contract suite against the released image.

## Local run

```bash
docker build --build-arg APP_VERSION=0.1.99 --build-arg VCS_REF=$(git rev-parse HEAD) \
  -f engineering/deployment/Dockerfile -t harvest:local engineering/codebase
```
