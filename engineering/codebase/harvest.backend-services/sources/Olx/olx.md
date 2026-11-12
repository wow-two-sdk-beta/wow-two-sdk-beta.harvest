# HarvestAdapters.Olx

OLX Uzbekistan source reader for Harvest. This package performs no network I/O itself:
`ICaptureReader` parses fetched HTML and `ILinkAvailabilityReader` supplies source-aware checks through Harvest's
paced, robots-aware fetcher. `AddHarvestListingSources` in HarvestAdapters.Module composes the source.

Supports public Russian/Uzbek category and search URLs, saved query filters, forward pagination, stable ad IDs,
description, seller, source attributes, explicit prices/currency, publication time, photos and native field snapshots.
Locale and slug changes preserve the ad ID. Original fields use `olx.ad.v1`, independently of `wow2.post.v1` mappings.

Unknown layouts, login/challenge pages and unrelated redirects do not establish deletion. Explicit unavailable-ad
markers and direct 404/410 responses feed the engine's existing disappearance policy. Incomplete search results
never establish that an ad was removed. No account, contact-reveal endpoint or phone API is used.
