# Runtime composition

The public repository owns the complete UI, API, engine and extension contracts. It builds and tests without any
extension checkout. A source runtime references these packages, registers its implementations, and serves the same
compiled console. Pipeline instances are registry data returned by the authenticated API.

## Standalone UI

From `engineering/codebase/harvest.frontend-services`:

```sh
pnpm install --frozen-lockfile
pnpm dev:demo
```

Demo mode injects an in-memory `HarvestClient` with synthetic sources, editable pipelines, posts, run transitions,
adapter metadata and resumable delivery. Changes live in one tab; Reset demo creates a fresh instance. It makes no
backend, source or provider requests and needs no API key. Demo activation is a build/dev mode, never a URL parameter.
The normal production console remains disconnected until a key is supplied.

For real runtime development, set `VITE_API_TARGET` and use `pnpm dev:http`. The development proxy forwards API calls
to that host. Deployed hosts serve the compiled console and API from one origin. An embedding frontend imports
`HarvestConsolePanel` through `@wow-two-beta/fe-harvest/admin` and may inject its own `HarvestClient`.
The optional `connectionManaged` prop leaves connection ownership with that caller. The demo uses it so its
synthetic client cannot be replaced by an accidental real API connection.

## Adapter discovery

`GET /api/sources/{sourceId}/capabilities` returns the selected adapter, supported work kinds, local readiness and
a declarative scope schema. It follows run planning's last-matching adapter selection. Source/container visibility
is authorized before metadata is resolved; another tenant's source returns 404. No source/provider request is made.

Adapters optionally implement `ISourceConfigurationProvider`. Without one, the runtime supplies generic URL/query
guidance. Without an installed adapter, readiness is `unavailable` and operations are empty.

The shared console displays this metadata, applies URL requirements/guidance, and prevents newly enabling unsupported
schedule kinds. Existing unsupported schedules can be disabled. The Run action requires locally configured capture
capability. Scope schemas describe inputs; server-side validation, permissions, pacing and refusal handling remain
authoritative. Readiness is local configuration evidence, not confirmation of remote access.

Metadata contains labels, schemas and readiness messages. It contains no credential/session values or implementation
source. A host owns its connections; source-specific execution code is never loaded into the browser.

## Artifact boundary

`apps/web/dist` includes `harvest-console.json`: contract `harvest.console.v1`, app version and optional public
source revision. `APP_VERSION` and `HARVEST_SOURCE_REVISION`/`VCS_REF` stamp provenance, not environment settings.
Hosts consume the built directory through `HarvestConsoleRoot` instead of copying UI source.

An extension repository pins its reviewed public revision and exact published backend package version. It owns only
its implementation packages, configuration/provisioning and thin host. Public Harvest never references it. UI changes
ship through the next reviewed public build; private adapter changes do not require a UI fork.
