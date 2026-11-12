# flows/

Technical flows; one grows into its own `{flow}-flow.md` once it needs more than a list. Product flows:
`product/flows/flows.md`.

## Source run

1. An operator provisions Ocharo's channels and their disabled schedules (`ocharo setup` or the setup route).
2. The engine plans and runs work through the registered source adapter and, when configured, a leased transport.
3. The reader returns source evidence; extraction and the store keep raw captures and normalized posts.
4. Ocharo's `ocharo-fashion-v1` predicate selects candidates for resumable pull delivery; SCFD writes to no consumer.

## Access

Browser identity uses Hosting's BFF and an explicit organization-to-tenant binding. Service automation uses scoped
tokens or API keys. Source and provider secrets stay server-side; capability discovery exposes only display-safe
readiness.
