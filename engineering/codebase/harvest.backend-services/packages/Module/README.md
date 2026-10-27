# WoW2.Sdk.Beta.Be.Harvest.Module

Form 1 of the Harvest capability: `IGreetingsBroker` in the consumer's process, over the consumer's database. The
consumer owns the tables and applies the embedded `Migrations/NNN-name/{Apply,Rollback}.sql` with the be sdk migrator.

```bash
dotnet add package WoW2.Sdk.Beta.Be.Harvest.Module --prerelease
```

```csharp
services.AddHarvest().Configure(options => options.ConnectionString = "Data Source=app.db");
services.AddDatabaseBespokeMigrations(typeof(HarvestModuleAssembly).Assembly, migrations => { /* provider, version */ });
```
