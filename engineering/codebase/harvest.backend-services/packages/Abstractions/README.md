# WoW2.Sdk.Beta.Be.Harvest.Abstractions

The Harvest contract: `IGreetingsBroker`, `GreetingModel`, `GreetingConstants`, `GreetingRouteConstants` and the
`GreetingErrors` catalogue. A product's Domain and Application projects reference this package only; its composition
root picks an implementation:

- `WoW2.Sdk.Beta.Be.Harvest.Module` — Form 1, in process, over the product's own database.
- `WoW2.Sdk.Beta.Be.Harvest.Client` — Forms 2 and 3, over the running Harvest service.

```bash
dotnet add package WoW2.Sdk.Beta.Be.Harvest.Abstractions --prerelease
```
