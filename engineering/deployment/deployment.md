# Harvest API deployment

`Harvest.Api` runs the generic capture and delivery API and worker. Private source runtimes use the same Hosting
package and deployment contract.

## Local Compose

```sh
docker compose -f engineering/deployment/docker-compose.yml up -d --build --wait
curl --fail http://127.0.0.1:8397/health
```

Compose uses PostgreSQL 18 and a named database volume. Both containers restart unless stopped. The local operator
key comes from the Compose environment. The API listens on loopback port 8397.

## Shared API access

Register one Harvest resource in the Harvest environment's Identity issuer. Its HTTPS URI, including the trailing
slash, is the API audience. Register separate confidential `client_credentials` clients for the operator and each
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

Replace the sample tenant ID with an active Harvest tenant. Register `tenants.manage`, `sources.manage` and
`events.read` in Identity for this example. Harvest intersects the dot-separated token scopes with the
configured registry scopes and applies tenant ownership at each endpoint. Consumer credentials belong in each
consumer's backend vault. Harvest needs only the issuer, audience and grant mapping.

## Wheelhouse

- The descriptor exposes `/api` on the `app` site. The service health probe calls `/health` over the platform network.
- The image is built through shared pipelines `v0.15.0`. CI builds the release bundle. Wheelhouse v0.12+ runs
  migration, configuration check, rollout and verification.
- Target settings in `/app/appsettings.Target.json` include `DatabaseSettings:ConnectionString`, the service access
  settings above and ingress trust via `Deployment:TrustedProxies` or `Deployment:TrustedNetworks`.
- PostgreSQL requires UTF-8 and `pg_trgm`. `dotnet Harvest.Api.dll migrate sets --json` lists the `harvest` set.
  `migrate check` checks rollback eligibility. Local boot applies migrations. Deployed boot checks the schema level.
- `dotnet Harvest.Api.dll config check` validates target settings. The dev probe checks `/health` and
  `/api/system/status` with `Host: localhost`. The status response reports the image version and source revision.
