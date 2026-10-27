# Capabilities/

The inline capability layer — iteration 2, the fetch lane. Built here behind Social- and core-shaped contracts and moved
out at the next +0.1: egress to core `Http/Egress`, sessions to Social; only the namespace changes.

| Implements (Abstractions) | Slice 1 |
|---|---|
| `Capabilities.Egress.IEgressPool` · `IEgressClientFactory` | direct egress over core `Http/Safety`; paid kinds declared, unavailable |
| `Capabilities.Sessions.ISessionLeases` | anonymous run and host jars, sealed at rest |
| `Capabilities.Profiles.IProfileCatalog` | `bot` and `browser:chromium-desktop` |
| `Capabilities.Challenges.IChallengeDetector` | the generic detector; adapters add their own |
| `Capabilities.CapabilityRungs` | the ladder engine over `harvest_host_lane`: `R0`–`R1` live, later rungs `capability-unavailable` |

- Owns: `harvest_host_state` (with the robots cache), `harvest_host_lane`, `harvest_usage` rows for egress meters.
- Rules: rate limits pace and never promote; three challenges or blocks promote one rung; a probe after 24 h demotes;
  no generic HTTP resilience on fetch clients.
