# WoW2.Sdk.Beta.Be.Harvest.Client

Forms 2 and 3 of the Harvest capability: `IGreetingsBroker` over the running Harvest service's HTTP API. The product
changes form by configuration: swap `AddHarvest()` for `AddHarvestClient(…)` in its composition root; no other code
changes.

```bash
dotnet add package WoW2.Sdk.Beta.Be.Harvest.Client --prerelease
```

```csharp
services.AddHarvestClient(options => options.BaseAddress = new Uri("https://harvest.example.com/"));
```
