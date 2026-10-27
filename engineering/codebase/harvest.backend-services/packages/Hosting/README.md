# WoW2.Sdk.Beta.Be.Harvest.Hosting

The shared Harvest HTTP host: API-key access, tenant isolation, controller routes, startup migration, health and SPA
composition. The generic service and private source host reference this package rather than copy its API.

```csharp
using Harvest.Api.Configurations;

var builder = WebApplication.CreateBuilder(args);
builder.Configure();
// Register named-source packages and configured integration providers here, explicitly.
var app = builder.Build();
await app.ConfigureAsync();
app.Run();
```

The `Harvest.Api.*` namespaces remain stable for existing host consumers. Named-source registration runs after
`Configure`, so explicit source adapters and transport bindings take precedence over generic defaults.
