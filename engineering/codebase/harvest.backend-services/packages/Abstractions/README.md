# WoW2.Sdk.Beta.Be.Harvest.Abstractions

The Harvest contract. A product's Domain and Application projects reference this package only; its composition root
picks an implementation:

- `WoW2.Sdk.Beta.Be.Harvest.Module` — Form 1, in process, over the product's own PostgreSQL database.
- `WoW2.Sdk.Beta.Be.Harvest.Client` — Forms 2 and 3, over the running Harvest service.

| Namespace | Holds |
|---|---|
| `Registration` | `IRegistrationBroker` — tenants, API keys, containers, sources, pipelines — its routes and errors |
| `Tenants` · `Containers` · `Sources` · `Pipelines` | the registry's models, posture switches, capability policy, schedules, step overrides |
| `Steps` | the step-broker contract source adapters implement: `ISourceAdapter`, `IStepBroker`, `IStepContext`, requirements, outcomes |
| `Capabilities.*` | `IEgressPool`, `IEgressClientFactory`, `ISessionLeases`, `IProfileCatalog`, `IChallengeDetector`, the rung ladder |
| `Readers` | `ICaptureReader` and its output, `Capture` |
| `Extraction` | `IPostExtractor` and the base envelope `wow2.post.v1` (`PostEnvelope`) |
| `Posts` · `Runs` · `Delivery` | link and post states, run vocabulary, subscriptions, events and the cursor page |

```bash
dotnet add package WoW2.Sdk.Beta.Be.Harvest.Abstractions --prerelease
```
