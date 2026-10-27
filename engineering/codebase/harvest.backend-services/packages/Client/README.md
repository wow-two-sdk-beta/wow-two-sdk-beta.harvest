# WoW2.Sdk.Beta.Be.Harvest.Client

Forms 2 and 3 of Harvest: the contract's brokers over the running Harvest service's HTTP API. The product changes form
by configuration: swap `AddHarvest()` for `AddHarvestClient(…)` in its composition root; no other code changes.

```bash
dotnet add package WoW2.Sdk.Beta.Be.Harvest.Client --prerelease
```

```csharp
services.AddHarvestClient(options =>
{
    options.BaseAddress = new Uri("https://harvest.example.com/");
    options.ApiKey = secrets["Harvest:ApiKey"]; // hv_…, scoped to what the product does
});
```

- `IRegistrationBroker` — tenants, API keys, containers, sources, pipelines; sent with the key in `X-Api-Key`.
