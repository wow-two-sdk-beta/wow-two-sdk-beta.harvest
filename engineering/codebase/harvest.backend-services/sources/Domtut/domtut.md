# HarvestAdapters.Domtut

Domtut (domtut.uz) source reader for Harvest. This package performs no network I/O itself: `ICaptureReader` parses
fetched HTML and `ILinkAvailabilityReader` supplies source-aware checks through Harvest's paced (3 s), robots-aware
fetcher. `AddHarvestListingSources` in HarvestAdapters.Module composes the source.

Reads the server-rendered new-build catalog (`/catalog-nedvijimosty`, its sections, the `*-class` pages, `/uz/`
included) with forward `?p=` pagination and saved filters, and residential-complex pages under `/nedvizhimost/{slug}`.
The key is the page's numeric post ID (`.property[data-id]`), so a renamed slug keeps the post. Extracts title,
description, the published "from" price and currency, per-m² price, developer, address and district, coordinates,
specs, layouts, labels and photos (full `property/` photos over `thumbs/` twins). Original fields use `domtut.ad.v1`.
Domtut states no publication time. `/filter/` searches, rent included, are JavaScript shells and are not fetched.

Direct 404/410 responses, a non-published post status and a not-found page at the requested listing feed the
disappearance policy. Removed listings redirect to `/404`, which robots.txt disallows, so such a redirect stays
inconclusive; so do unknown layouts, challenges and unrelated redirects. No contact-reveal endpoint is used.
