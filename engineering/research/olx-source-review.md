# OLX source port and verification

Reviewed: 2026-10-27. OLX implementation belongs to the private adapters repository; generic parsing and fetch-policy
seams belong to public Harvest. ListingShelf remains the read-only reference implementation.

## Implementation

`HarvestAdapters.Olx` ports the legacy broker's public route recognition, stable ad IDs, DOM/JSON-LD extraction,
query preservation, pagination and CDN photo deduplication. The source package depends on Harvest.Abstractions and
AngleSharp. It performs no I/O; Module's `AddHarvestListingSources` composes it with Harvest's existing fetcher,
storage, run budgets and delivery. The source host registers the marketplace composition independently of Ocharo.

| Surface | Behavior |
|---|---|
| Source | `olx-uz`, family `site`, OLX Uzbekistan public HTTPS routes |
| Saved scope | Category, search or seller catalog URL with the caller's exact query filters |
| Enumeration | Main results grid; ad IDs deduplicate promotions and locale/slug variants |
| Pagination | Next page must advance on the same host/path; omitted filters are inherited; changed filters, duplicate page parameters and cycles are rejected |
| Completeness | Always a search window, never evidence that an omitted ad was deleted |
| Capture | Title, complete description, price text, structured price/currency, displayed seller, parameters and source photos |
| Dates | Native `createdTime` is publication time; refresh/expiry times and relative display labels remain separate |
| Initial fields | `olx.ad.v1`, parser `olx-html@1`; public ad fields, original Product JSON-LD and raw source labels |
| Mapped fields | `wow2.post.v1`, kind `listing`; listed price, seller, place, media, contacts explicitly published in description, and extraction evidence |
| Photos | Published CDN URL references, deduplicated by file identity; no invented original URL or image download step |
| Semantic changes | Content, price, attributes and photos drive revisions; refresh time, expiry extension and session state do not |
| Delivery | Ordinary tenant subscription; OLX is not inserted into the Ocharo event stream |

The source's [robots policy](https://www.olx.uz/robots.txt) permits public catalog/ad pages while restricting account,
chat, contact and other routes. `IFetchDestinationPolicy` additionally validates initial page URLs and each redirect
before transport. No login, private API, hidden phone reveal, CAPTCHA solver or paid integration is used.

Schema.org [Product](https://schema.org/Product) and [Offer](https://schema.org/Offer) provide the shared price/seller
vocabulary. Product offers map to listed prices rather than event tickets. Currency comes from explicit source data;
Harvest does not calculate an exchange rate. If the visible price differs in presentation from JSON-LD, both original
values remain inspectable in the native snapshot. Negotiability and category-specific attributes also remain there.

## Availability and identity

The historical ListingShelf URL for ad `3Rp02` was fetched and redirected to an updated slug with the same ID. The ad
was still active. URL slug changes are therefore not deletion evidence; identity uses the published OLX ad ID.

An OLX check accepts a matching active detail response, recognizes explicit inactive/unavailable markers and matching
native inactive state, and leaves ambiguous pages inconclusive. A challenge, different-ad redirect, category landing
or unrecognized markup cannot establish that the known ad is present or gone. Conflicting active/inactive evidence
also remains unresolved. `ILinkAvailabilityReader` supplies these source rules to the common check step.

Direct HTTP 404/410 retain transport evidence. The engine's existing policy requires another independent strong
negative before 404/soft-404 becomes gone; HTTP 410 can establish disappearance immediately. A later positive can
restore the link. Source existence is distinct from whether an item was sold or a seller temporarily deactivated it.

A synthetic nonexistent-ad control, explicitly not a previously captured seller ad, returned a real HTTP 404. This
validates OLX's missing-route response, not a claim that a particular real seller removed an ad. The HTTP-200 inactive
variants and suspect/gone/relisted transitions are verified with source-shaped fixtures; no seller ad was modified.

## Verification

The new reader suite covers public routes, locale variants, saved filters, pagination boundaries, duplicate ads,
photos, native timestamps, partial extraction, relative-time ambiguity, unavailable markers, conflicting status,
challenge/unknown responses and cancellation. The PostgreSQL pipeline test traverses two filtered fixture pages,
stores two distinct posts, pulls durable delivery, preserves link state after inconclusive checks, and verifies
suspect → gone → relisted through independent check runs. Tests never open ListingShelf's live archive database.

The live check is intentionally bounded to one listing page and at most two detail captures. It verifies the real
source through the full engine; it is not a full market crawl. The current engine's `MaxPages` budget counts all work
pages, including detail steps. The reusable command uses a budget of three work pages and two captured items.
Pagination traversal is covered by the two-page engine fixture and the real page's extracted next-link shape.

### Recorded live result

The run `01a17eb6-2dbc-7b04-8021-984e8a37ccb7` captured two listings at 2026-10-27 23:53:59–23:54:01
Asia/Tashkent, with zero failed steps and two durable delivery events. It used four network requests, including
robots, and three work pages: one results page and two detail pages. Its `partial / pageLimit` completion is the
intentional configured boundary, not a claim of exhausted results.

Both records retained their descriptions, seven photo references each, explicit USD prices, displayed seller,
location, native ad parameters and exact creation time. Neither exposed a public phone number in the description;
no contact-reveal request was made. The native snapshots preserve refresh/expiry information separately from the
mapped publication date. The local evidence artifact is `/tmp/harvest-olx-live-result.json`.

### Reusable local run

Start `HarvestSources.Api` with `Harvest__Worker__Enabled=true`, an operator key and the local PostgreSQL connection.
Paid integrations and Instagram can remain disabled. Then, from `engineering/codebase/harvest-adapters.backend-services`:

```sh
HARVEST_API_KEY='<local operator key>' python3 tools/verify-olx-live.py \
  --url 'https://www.olx.uz/oz/nedvizhimost/kvartiry/tashkent/?search%5Bfilter_float_price%3Ato%5D=10000000' \
  --output /tmp/harvest-olx-live-result.json
```

The script creates or reuses its own private verification tenant/container, registers a source and manual pipeline,
subscribes to ordinary post delivery, starts a bounded run, and saves results and evidence. It rejects remote API
targets, enabled schedules, AI-enabled settings or unexpected limits; credentials are read from the environment and
never written into the artifact. A repeated source filter reuses its pipeline. It does not change an existing
tenant's Ocharo setup or ListingShelf data.

### Limits

No full-market pagination scan or natural seller-deletion lifecycle was performed. A hidden phone number remains
unknown. JSON-LD and hydration markup can change; unrecognized shapes stop extraction/check evidence rather than
becoming empty searches or deleted posts. Category-specific attributes are retained as initial fields and are not
yet mapped into a housing, vehicle or fashion-specific consumer schema.
