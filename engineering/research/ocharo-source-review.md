# Ocharo event sources

*Checked: 2026-10-27, Asia/Tashkent. Read-only research; no registration, outreach or paid provider call.*

## Recommendation

Afisha is first by the owner's decision. Add **ANONS**, **Hunarmand/Craftfair**, and **Uzexpocentre** next.
This is a recommendation for complementary Ocharo coverage, rather than a claim that an exhaustive volume survey
proved these the three largest publishers. Public calendars, artisan participation calls and official venue dates
serve different needs; their records still require cross-source deduplication and review.

- **ANONS:** local markets, shows and event discovery alongside Afisha.
- **Hunarmand/Craftfair:** Uzbek regional and international artisan opportunities, including application deadlines.
- **Uzexpocentre:** primary venue dates for clothing, textile, leather/accessory and runway events.

## Ocharo filter

The consumer intent is Ocharo's `product/features/events.md` and `engineering/architecture/events-contract.md`
in the `ocharo-platform` checkout identified by the hub repository registry. That checkout stays independent.

- include clothing and designer showcases, runway, garment/textile sourcing and trade fairs, clothing or handmade
  accessory markets, and relevant design/craft competitions or workshops
- use Uzbekistan as home, including regional cities; retain relevant Central Asian and international opportunities
- classify jewellery, home textiles and broader craft as adjacent when their relevance is established
- exclude concerts, cinema, sport, generic food/tourism events and ordinary retail discounts
- preserve visitor, seller, exhibitor, designer/runway and artisan participation as separate evidence
- keep unknown dates, costs, deadlines and eligibility unknown; an event listing or ticket does not establish admission
- store the publication date separately from event dates; recaps do not become new future events
- collect candidates for review; never apply, register, buy tickets or publish them automatically

## Added source presets

### ANONS

- key: `anons-uz`; host: `anons.uz`; seed: `https://anons.uz/ru/events`
- secondary discovery: `https://anons.uz/ru/`; localized detail identities reduce to one event slug
- relevant sample: [Park in Mall Market](https://anons.uz/ru/events/yarmarka-park-in-mall-market-1)
- discovery: event anchors under `/ru/events/`; calendar links include `?day=nearest` and `?date=YYYY-MM-DD`
- detail: JSON-LD `@graph` includes `Event`, with explicit offset-bearing start/end timestamps; article metadata
  provides a different publication timestamp. Prefer the Event fields
- HTML fallback: `h1`, `.section-content.content`, and labelled `.section-more__content` blocks
- the sample advertises visitor admission, rather than an open seller application
- the inspected homepage contained 45 distinct event-detail URLs, including out-of-scope entertainment; this is a
  window count, not a fashion-volume count or proof of complete historical discovery
- lazy content exposes `.js-lazy-section[data-url]`; no numbered pagination was established
- completeness: `window`; absence from the calendar must not mark an event gone
- robots: [200 response](https://anons.uz/robots.txt), event routes not disallowed; search/profile/admin paths disallowed
- observed list/detail transport: direct HTTPS 200, no sign-in or CAPTCHA encountered

The sample Event dates are 31 October–1 November 2026, while its article publication date is 26 October. The detail's
free-entry wording describes visitors; seller fees and availability remain unverified.

### Hunarmand / Craftfair

- key: `craftfair-uz`; host: `craftfair.uz`; seed: `https://craftfair.uz/events?lang=en`
- additional lists: `/exhibitions?lang=en` and `/festivals?lang=en`; retain `lang` while requesting, normalize it for identity
- discovery: `article.event-card .event-card-title a`; the direct list contained five distinct event slugs
- relevant sample: [Craft Fair 2026](https://craftfair.uz/events/craft-fair-2026?lang=en)
- detail: `h1.h1`, `article.event-detail-panel`, `.sidebar-meta-item .meta-label` plus `.meta-val`
- admission state: `.status-badge` and the disabled application button, independently from an event's future dates
- the sample states event dates 25–27 November 2026, application cutoff 26 October, and closed applications
- no JSON-LD or numbered pagination was observed; keep completeness `window` until all catalogue routes are covered
- multilingual text can remain Uzbek inside an English page; do not translate or infer missing eligibility
- cross-year labels require explicit year evidence; conflicting labelled and narrative dates remain a review flag
- observed list/detail transport: direct HTTPS 200, no sign-in or CAPTCHA encountered
- [robots](https://craftfair.uz/robots.txt): wildcard `Allow: /`; named AI crawlers have separate disallow rules;
  content signals include `search=yes`, `ai-train=no`, `use=reference`. Preserve those signals and keep model use off

The [official catalogue](https://craftfair.uz/events?lang=en) complements city portals with Margilan/Khiva and foreign
artisan routes. A closed application remains useful history and must not be presented as a currently available place.

### Uzexpocentre

- key: `uzexpocentre-uz`; host: `www.uzexpocentre.uz`
- seed: `https://www.uzexpocentre.uz/en/menu/krgazmalar-rezhasi`
- parse the HTML calendar table under its explicit year/period heading, currently July–December 2026
- expand `rowspan` cells before mapping columns: the Texworld and Apparel rows inherit organizer, date and pavilion
  cells from the preceding Heimtextil row
- preserve separate co-located events; a shared date/organizer is not a duplicate identity
- sample programme: [venue calendar](https://www.uzexpocentre.uz/en/menu/krgazmalar-rezhasi), including apparel/textile
  fairs and the source-spelled `Viza Fashion Week` (row 19, 17–18 November under the 2026 heading); its calendar
  supplies venue dates, not exhibitor acceptance or application deadlines. Both the direct HTML snapshot and web
  rendering contain that row; raw HTML separates `Fashion` and `Week` with a closing `strong` tag
- stable external keys use calendar year and normalized event title; row numbers change when the venue edits its plan
- no JSON-LD or pagination was observed; page URL is provenance for each row, rather than an invented detail URL
- observed calendar: direct HTTPS 200. Robots first timed out; a later [200 response](https://www.uzexpocentre.uz/robots.txt)
  contained only `User-agent: *`. Do not interpret a timeout as an allow response
- retain source attribution; event date/venue changes need another observation and do not prove a delivered event

## Compared alternatives

- [iTicket](https://iticket.uz/en): useful for public ticket price and visitor availability. The inspected page exposes
  a broad entertainment catalogue with limited server-rendered event content; ticketing does not establish seller or
  runway access. Add later for corroboration rather than replacing the three chosen roles.
- [Spot exhibitions](https://www.spot.uz/ru/exhibitions/): useful business/sourcing coverage, including textile events,
  but its current list mixes many unrelated industries. It is a later discovery source, not primary participation evidence.
- [CAEx calendar](https://caexuzbekistan.com/events-calendar/?language=en): primary venue corroboration for Baraka
  Friday and selected adjacent events. Its current programme mixes many industries; add after the initial set.
- [CCA programme](https://ccat.uz/en/programme): valuable for specific fashion/craft exhibitions and learning, with
  a narrower institutional programme. It is not a replacement for market or trade-fair discovery.
- Gazeta/other news outlets remain discovery leads; this pass did not establish higher incremental coverage than
  the inspected sources, so no negative completeness or volume claim is made about them.

## Implementation acceptance

- seed Afisha first and the three additional sources as independent source/pipeline definitions
- limit discovery to each adapter's observed listing/detail routes and allowed origins
- use reduced synthetic fixtures that preserve selector/date shapes; raw research snapshots stay outside Git
- test Russian month/cross-month ranges, date-only precision and explicit timezone-bearing structured dates
- test closed calls, unknown costs, publication/event date separation, multilingual text and calendar rowspans
- filter each candidate by Ocharo relevance without discarding its supporting source facts
- deduplicate editions across portals and primary sources while preserving every original link and field source
- report live smoke results separately from fixture tests; direct access here does not guarantee future access
- configure optional proxy, CAPTCHA and session services only through explicit host settings; no credentials or paid
  calls were available or exercised in this research

## Evidence boundaries

The web index can lag direct HTML: Craftfair's indexed English list omitted an event that its fetched HTML contained.
Use the fetched page as the current observation and retain hashes/timestamps. Conversely, a readable page is not
proof that registration, ticket purchase or a vendor application works. No such external action was attempted.
