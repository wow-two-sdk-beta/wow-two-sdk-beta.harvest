# research/

Technical research — spikes, comparisons, vendor and source evaluations; one doc per investigation. An analysis batch
with its decision queue goes in `{slug}-analysis-{yyyy-mm}/`.

Each review keeps the wording of its date. Those from before 2026-11-12 came over with the source readers: their
`harvest-adapters.backend-services/packages/{Source}` paths now read `harvest.backend-services/sources/{Source}`, and
their public/private split no longer exists.

| Review | Covers | Date |
|---|---|---|
| [Ocharo event sources](ocharo-source-review.md) | Afisha plus ANONS, Craftfair/Hunarmand and Uzexpocentre; source and access traps, alternatives | 2026-10-27 |
| [Ocharo source registry](ocharo-source-registry.md) | the ranked inventory behind `ocharo-event-sources.json`: 100 publishers, 189 channels, collection mode per channel | 2026-11-09 |
| [Afisha field audit](afisha-field-audit.md) | extraction correction, native/mapped/AI layers, contacts and availability evidence | 2026-10-27 |
| [Instagram sources and setup](instagram-source-review.md) | reviewed fashion/craft publishers, official API configuration, coverage limits | 2026-10-27 |
| [OLX source verification](olx-source-review.md) | ListingShelf port, saved filters, ad identity, availability, bounded live-run evidence | 2026-10-27 |
| [OLX egress](olx-egress-options.md) | candidate proxy providers for the two OLX origins, terms and trial order | 2026-11-05 |
| [Provider configuration](integration-providers.md) | proxy/IP lease bridge, supplied account sessions, bounded 2Captcha v2 client | 2026-10-27 |
