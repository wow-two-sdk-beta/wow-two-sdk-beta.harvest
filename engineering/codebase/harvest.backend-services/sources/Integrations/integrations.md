# Harvest source integrations

Opt-in transport and challenge clients for named Harvest sources. Call
`AddHarvestIntegrations` after the generic Harvest fetching services and bind
`HarvestIntegrationOptions` from the host's configuration and secret provider.
Both `Enabled` and `Captcha.Enabled` default to `false`.

- `FetchRouteOptions` selects one direct, fixed-proxy, or IP-lease route for an
  exact scheme/host/port. It stays fixed for robots, fetches and allowed redirects.
  Configured route failures return capability unavailable; they do not select a
  new route or silently fall back to direct transport.
- Fixed proxies support explicit HTTP, HTTPS, SOCKS4, SOCKS4a and SOCKS5 settings.
  Proxy credentials stay in the proxy handler. The target and proxy must resolve
  to public addresses; the connection pins the validated proxy address.
- A fixed proxy route (the inline `OlxProxy` or a fixed profile) keeps one guarded client, and so one connection pool,
  for each source origin for the life of the process (`SharedProxyClients`): later fetches reuse its connections, which
  core's handler recycles every two minutes, instead of paying a new TCP connection, `CONNECT`, proxy authentication
  and TLS handshake. The client is keyed by the route and the proxy's effective settings (server, username, a hash of
  the password): a change builds a new client, and the replaced one is kept until shutdown so a fetch still reading
  through it is not cut off. The factory disposes every client at host shutdown. A fetch still takes its own lease and
  checks the enabled switch, the route match, the profile and the destination, so a disabled integration goes direct on
  its next fetch. A route that leases its IP from a broker builds a client for each lease and disposes it with the lease.
- A route with no account session and no caller jar reads a host's `robots.txt` once per route key and
  `RobotsCacheDuration`, not once per fetch (`IFetchTransportLease.CachesRobotsByRoute`); robots are per site, not per exit.
  See `packages/Module/Capabilities/capabilities.md`.
- `OutboundProxyBroker` in the backend SDK selects fixed profiles and implements
  the HTTPS lease-bridge contract with an allowlist and finite expiry.
- `AccountSessionPool` exclusively leases operator-supplied HTTPS cookies in this
  process. A session belongs to one exact origin, expires at its configured time,
  and performs no account creation or login.
- `ICaptchaSolver` implements 2Captcha JSON API v2 submission and result polling
  for reCAPTCHA v2 and Turnstile. The source broker must interpret and apply any
  returned token. Submission is explicit and separately enabled.

Provider requests use a separate guarded HTTP client and never inherit source
cookies or headers. Session and proxy fetches retain the engine's host
gates, robots rules, pacing, timeouts and request budgets. The remote proxy controls
its own destination DNS; operators must select a provider that applies the
required public-target policy remotely.

See `engineering/research/integration-providers.md` in this repository for the
bridge protocol, credential setup and verified provider documentation. Unit
tests use fake handlers and time; they make no provider calls.
