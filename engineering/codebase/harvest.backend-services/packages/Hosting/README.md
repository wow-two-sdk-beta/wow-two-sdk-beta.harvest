# WoW2.Sdk.Beta.Be.Harvest.Hosting

The shared SCFD HTTP host provides API-key access, tenant isolation, controller routes, startup migration and
health. The generic service and private source host both reference this package.

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
