# Configurable harvesting integrations

Verified against primary documentation on 2026-10-27. This implementation makes
no live provider call during configuration or tests. The operator will supply
credentials later. It creates no accounts, logs into no sites, and purchases
nothing.

## Composition and source scope

`AddHarvestIntegrations` replaces the public engine's transport factory after
`AddHarvestFetching`. The public engine keeps control of host permissions,
robots, request pacing, redirects, Retry-After, deadlines and claim budgets. The
factory acquires one lease for the requested origin and returns that lease to
the engine for its robots and fetch requests. Route failures do not rotate IPs,
switch accounts, or select direct transport automatically.

An origin means its exact scheme, IDNA host and effective port. It is configured
without a path, query or fragment. A redirect to any other origin is rejected by
the private lease before a network call. Account cookies additionally require
HTTPS. All integration and CAPTCHA settings start disabled; an unconfigured
origin continues to use the public engine's guarded direct client.

The host binds `HarvestIntegrationOptions` from its configuration section. This
example deliberately contains no secret and enables no provider:

```json
{
  "HarvestIntegrations": {
    "Enabled": false,
    "Routes": [],
    "Sessions": [],
    "Captcha": {
      "Enabled": false,
      "BaseUrl": "https://api.2captcha.com/",
      "AllowedOrigins": [],
      "PollInterval": "00:00:05",
      "SolveTimeout": "00:02:00",
      "MaximumPolls": 24
    }
  }
}
```

## Proxy profiles and IP leases

Set `HarvestIntegrations:ProxyProfilesJson` in the target vault. Each name selects
a fixed endpoint or the SDK's HTTPS lease-broker contract.

```json
{
  "geonode-uz": {"server": "https://proxy.example:443"},
  "decodo-uz": {"server": "socks5://proxy.example:1080"},
  "lease-uz": {
    "broker": {
      "baseUrl": "https://broker.example/v1/",
      "apiKey": "<vault-supplied broker key>",
      "allowedProxyHosts": ["proxy.example"]
    }
  }
}
```

Use each account's generated endpoint and fill its optional `username` and
`password` fields in the vault. Set `OlxProxyProfile` to one name. Other source
routes can use `Mode: "Proxy"` with `ProxyProfile` in place of inline settings.
Recreate the Harvest service after switching profiles. Harvest uses the SDK's web-read
policy. SDK GraphQL and gRPC clients select their own endpoint policies.

A fixed route has `Key`, `Origin`, `Mode: "Proxy"` and
`Proxy: { "Server": "https://proxy.example:443" }`. The operator supplies
`Username` and `Password` through a host secret provider. Credentials embedded in
a proxy URL are rejected. Supported proxy schemes are HTTP, HTTPS, SOCKS4,
SOCKS4a and SOCKS5, consistent with [.NET proxy support](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.defaultproxy?view=net-10.0).
The implementation uses an explicit proxy instance; it does not modify global
proxy configuration or rely on ambient proxy environment variables.

Both the target and the proxy endpoint must resolve entirely to public IP
addresses using the SDK's canonical address validator. The actual socket pins
the validated proxy endpoint. A remote proxy resolves/connects to its target
itself; local code cannot pin that remote DNS resolution. Use an approved proxy
whose egress policy also rejects private targets. Private, loopback and
link-local proxy endpoints are rejected.

The SDK broker supports an operator-provided HTTPS bridge. Use the `broker`
profile field, or the inline `IpLease` field with `Mode: "IpLease"`.

| Setting | Meaning |
| --- | --- |
| `BaseUrl` | HTTPS bridge base, optionally with a path prefix |
| `ApiKey`, `ApiKeyHeader` | Bridge secret and header name; default header is `X-Api-Key` |
| `AcquirePath` | Relative POST path, default `leases/acquire` |
| `ReleasePath` | Relative POST path, default `leases/release` |
| `AllowedProxyHosts` | Exact permitted returned proxy hosts; empty rejects all leases |

Acquire request:

```json
{ "origin": "https://source.example", "routeKey": "source-proxy" }
```

Acquire response:

```json
{
  "leaseId": "opaque-provider-id",
  "proxyUri": "https://proxy.example:443",
  "username": "operator-supplied-by-bridge",
  "password": "operator-supplied-by-bridge",
  "expiresAtUtc": "2030-01-01T12:00:00Z"
}
```

Release request to the configured release path:

```json
{ "leaseId": "opaque-provider-id" }
```

Lease IDs allow ASCII letters, digits, `_` and `-`, up to 128 characters. Returned
proxy hosts must match the configured allowlist, and the expiry must be in the
future. The client performs one acquire and one best-effort release, with no
automatic allocation retry. A failed or ambiguous acquire is reported without
another paid call. Providers must enforce finite expiry: release failures or
invalid acquire responses cannot establish that the remote lease was released.

## Operator-supplied account sessions

Routes can specify `SessionIds`. Each matching `Sessions` entry has a unique
`Id`, exact HTTPS `Origin`, `ExpiresAtUtc`, and a `Cookies` list containing
`Name`, `Value` and optional `Path` (default `/`), with at most 100 cookies per
session and a 16 KiB size limit per cookie. Cookie values belong in a host
secret provider. Domain widening, cross-port reuse and HTTP downgrade are not
permitted. Cookies set by the source during a lease remain in that session's
in-memory jar, and expired sessions cannot issue further requests.

The pool grants each session to at most one fetch lease at a time and releases
the slot when that lease is disposed. This is process-local concurrency. Hosts
that run multiple service instances must partition session IDs per instance or
provide shared coordination before sharing the same account. No browser
automation, password handling, account registration, or login flow is included.

## 2Captcha API v2

`ICaptchaSolver` uses 2Captcha's documented [createTask](https://2captcha.com/api-docs/create-task)
and [getTaskResult](https://2captcha.com/api-docs/get-task-result) JSON endpoints.
The source broker explicitly submits a supported task, then applies the token
according to that source's supported challenge flow. The generic fetcher never
submits a challenge automatically.

The implementation supports the documented [reCAPTCHA v2 proxyless task](https://2captcha.com/api-docs/recaptcha-v2)
and [Turnstile proxyless task](https://2captcha.com/api-docs/cloudflare-turnstile).
Each request includes an HTTPS `WebsiteUrl` on `AllowedOrigins` and a site key;
optional task-specific fields include reCAPTCHA `Invisible` or Turnstile
`Action`, `Data` and `PageData`. The URL query and fragment are omitted from the
provider payload. Source cookies, authorization headers and proxy credentials
are never copied to the CAPTCHA client.

The solver submits once, waits at least five seconds between result polls, and
stops at the configured poll count or solve deadline. It preserves a returned
task ID so a caller can inspect a known task without submitting again. Provider
errors become bounded error codes; response descriptions and secrets are not
included in failures or diagnostic `ToString()` output. JSON responses have a
64 KiB limit.

A Turnstile token does not by itself establish that a general Cloudflare
challenge has been cleared. Challenge pages can also require browser callbacks,
browser/session state and a matching user agent; source-specific code must
support that complete flow before enabling automatic token application. Afisha
currently uses its accessible public listing flow and does not call a paid
solver.

## Setup gates and verification

Before enabling a source route, the operator supplies its exact approved
origin, a fixed proxy or a bridge URL/key and permitted proxy hosts, and any
existing HTTPS account-session cookies with their expiry. Before enabling
CAPTCHA submission, the operator supplies a 2Captcha key, its exact allowed
origins, and a source adapter that knows how to apply the result. Store secrets
outside the repository and do not include them in harvested evidence or logs.

Unit verification uses fake HTTP handlers, proxy factories, DNS resolvers and a
fake clock. It covers protocol bodies, bounded polling, cancellation, separate
provider headers, exact-origin and expiry guards, account exclusivity, one-time
lease release, invalid proxy addresses, and absence of fallback after a route
failure. Live provider compatibility remains an operator setup check after
credentials and an explicit source route are supplied.
