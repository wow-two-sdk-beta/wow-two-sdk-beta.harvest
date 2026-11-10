# WoW2.Sdk.Beta.Be.Harvest.Testing

The Harvest test kit:

- `FakeRegistrationBroker` — an in-memory `IRegistrationBroker` for a consumer's unit tests; it passes the conformance
  suite, and `IssuedSecrets` keeps every key it minted.
- `RegistrationBrokerConformance` — the registry's suite: derive a test class, supply the broker and its clock, and every
  rule of the contract runs against that implementation. Cases make their own tenants, so a shared store is fine.
- `FakePostBroker` and `FakeDeliveryBroker` — captures, provenance, merges and pull delivery over the same registry fake.
- `PostDeliveryBrokerConformance` — runs the shared post/delivery contract against the module, HTTP clients and fakes.
- `FakeRunBroker` and `RunBrokerConformance` — queue controls and reading paces without source network calls, and the
  shared run contract.
- `FakeSourceOverviewBroker` and `SourceOverviewBrokerConformance` — the console's Sources reads over the other fakes,
  by the module's rules; `FakeRegistrationBroker.SetNextDue` stands in for the scheduler's due times.

The fakes live in the `Testing.Posts`, `Testing.Delivery`, `Testing.Runs` and `Testing.Sources` namespaces. A fake post broker's
`RegisterRun` method supplies a run reference when a consumer tests run-attributed ingest.

```bash
dotnet add package WoW2.Sdk.Beta.Be.Harvest.Testing --prerelease
```
