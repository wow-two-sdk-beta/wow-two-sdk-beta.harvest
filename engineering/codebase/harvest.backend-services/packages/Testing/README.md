# WoW2.Sdk.Beta.Be.Harvest.Testing

The Harvest test kit:

- `FakeRegistrationBroker` — an in-memory `IRegistrationBroker` for a consumer's unit tests; it passes the conformance
  suite, and `IssuedSecrets` keeps every key it minted.
- `RegistrationBrokerConformance` — the registry's suite: derive a test class, supply the broker and its clock, and every
  rule of the contract runs against that implementation. Cases make their own tenants, so a shared store is fine.

```bash
dotnet add package WoW2.Sdk.Beta.Be.Harvest.Testing --prerelease
```
