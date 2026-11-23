# Ocharo source registry and collection review

*Last updated: 2026-11-24*

The [registry](../codebase/harvest.backend-services/sources/Module/Ocharo/ocharo-event-sources.json)
holds 121 publishers and 219 reviewed channels. It keeps priority separate from
whether a collector can run. Research citations, past editions and non-selected links stay in the business
event index. A publisher can have several channels; shared social or government hosting does not make them the same publisher.

## Instagram additions

The existing collection study verified Business Discovery for these publishers. Each route needs an explicit
publisher-group selection.

| Publisher | Handle | Coverage |
|---|---|---|
| Art Time | `art_time_uz` | Art Bazaar and maker markets |
| Art Station | `artstationuz` | Exhibitions, talks and workshops |
| CAEx Baraka Friday | `caex_baraka_friday` | Retail fair and exhibitor notices |
| Tashkent Fashion and Textile Expo | `fashionexpouz` | Trade fairs and exhibitor programmes |
| Art and Culture Development Foundation | `acdfuz` | Exhibitions, festivals and craft programmes |
| Uzbekistan Fashion Week | `ufw.official` | Runways and fashion-week programmes |
| Tashkent Design Week | `designweekuz` | Design fairs and talks |
| Global Jewelry Fair | `jewelryfair.uz` | Jewellery exhibitions and exhibitor notices |
| Afisha.uz | `afishauzb` | Editorial discovery of design fairs and exhibitions |

Fresh local captures returned 12 public posts each from Goethe-Institut Uzbekistan, Navoiy Tourism and Uzbekistan
Travel. Goethe adds arts programmes, Navoiy adds regional festivals and design calls, and Uzbekistan Travel adds
TITF programmes. [Goethe calendar](https://www.goethe.de/ins/uz/de/m/ver.cfm) · [Official regional channels](https://api-portal.gov.uz/uploads/146/2025/12/30/b892229e-d050-2c7f-5c4b-167fc4d13c79_media_29997.pdf) · [Uzbekistan Travel](https://uzbekistan.travel/en/o/take-part-actions-promoting-tourism-potential-uzbekistan/)

Meta refused the Qashqadaryo Tourism, Iteca and Textile Association probes. The sampled Craft & Culture handle
returned a different publisher, so the registry omits it.

| Source | Collection decision | Evidence limit |
|---|---|---|
| [Afisha fashion](https://www.afisha.uz/ru/fashion) and exhibitions | Discover scoped links; read embedded Event data and details. Keep one locale-neutral event identity. | Use as discovery. Editorial coverage is not a new event or an application. |
| [ANONS](https://anons.uz/ru/events) | Read the events index and linked Event JSON-LD. Keep locale aliases and article publication separate. | A visitor listing does not establish seller access. |
| [Hunarmand/Craftfair](https://craftfair.uz/events?lang=en) | Read the all-events index and event details. The 9 November fetch included Atlas Bayrami and Craft Fair. The category and locale views remain references to avoid duplicate polls. | Read deadline and call status from labelled detail fields. Association announcements on the separate official site still need their own reader. |
| [Uzexpocentre](https://www.uzexpocentre.uz/en/menu/krgazmalar-rezhasi) | Read rows under the printed calendar year, expand merged cells, and retain pavilions and separate co-located fairs. | Dates and venue evidence; no inference about a booth or invitation. |
| [CAEx](https://caexuzbekistan.com/events-calendar/) | Read the public WordPress calendar's event rows, titles, date labels, organizers and first-party permalinks. This route can be selected now. | The reader polls one calendar window. Permalink path dates are not occurrence dates; absent rows are not cancellations. The Baraka microsite remains separate first-party evidence for seller terms. |
| [Iteca](https://iteca.uz/en/calendar-of-events) | Organizer calendar cards link to each edition site, visitor badge and stand enquiry. The web index showed cards; a direct HTML request on 9 November returned a browser challenge. Keep the calendar in manual review and assess each official edition site independently. | A stand enquiry or a visitor badge does not confirm exhibitor access. No challenge bypass. |
| [Teplo](https://teplo.world/) | Programme and ownership reference. Review Market, Fest and store pop-ups separately. The first two have opt-in publisher routes via the existing official Instagram integration. | Publisher API eligibility and current posts are unverified. Store reposts are corroboration; post time is not event time. |
| [Tourism Committee](https://gov.uz/en/uzbektourism/news/events) | Calendar view showed filters without event rows. Evaluate the committee news and [National PR Centre](https://uzbekistan.travel/en/c/announcements/) streams independently. Use the same section-scoped approach for city and regional authority pages. | Use the original issuer notice for dates, call rounds, eligibility and deadlines. |
| [Idem](https://idem.uz/c/exhibition/) and ticket sellers | Idem supplies dated discovery and references to independent upstreams. Ticket pages can supply visitor admission and price; add a reader only if it adds distinct coverage. | Aggregation never transfers authority or schedules. Seller access needs the organizer. |

A `harvest` channel reaches its collector through its `adapterKey`, one of the profiles the host routes to: the five
named readers above, Instagram's opt-in provider route, the Telegram preview route (Wave 2, below) and three generic
routes over the generic readers (`packages/Module/Readers/readers.md`), each at authority 60: `generic-feed` (RSS, Atom,
ICS; no deletion check), `generic-structured` (pages with JSON-LD `Event` data) and `generic-site` (a sitemap), the last
two with the daily `web-direct` check. A generic channel needs no new reader but follows the same rules: an owned
channel, a unique HTTPS seed and `setup` `default` or `selected`; Wave 1 (below) switched 25 channels to them. A source
reaches its reader by its family, so a generic route on a host a named reader serves runs that named reader. Every
profile captures on the cron `0 1,7,13,19 * * *` in `Asia/Tashkent`.

### Wave 1: generic routes (2026-11-21)

25 channels of 22 publishers take a generic route with `mode` `harvest` and `setup` `default`: 7 `generic-feed`,
1 `generic-structured` and 17 `generic-site`. The probe ledger of 2026-11-21 (`source-probes.md`, wow-two-ws)
proposed 29. Each feed was fetched once that day under the identified agent, inside `robots.txt`, and every one parsed
with dated items (6 RSS, 1 iCalendar). A fixture parse and a feed read are no live capture; the channels run only after
`ocharo setup` provisions them.

| Channel | Route | Pipeline seed |
|---|---|---|
| `caex/baraka-retail-fair` | `generic-feed` | `https://caex-barakafriday.com/feed/` |
| `human-house/primary` | `generic-feed` | `https://human-house.com/feed/` |
| `idem/festival-listings` | `generic-feed` | `https://idem.uz/cal/c/festival.ics` |
| `hd-magazine/primary` | `generic-feed` | `https://mag.humodoc.com/ru/feed.xml` |
| `kultura-uz/primary` | `generic-feed` | `https://www.kultura.uz/rss.cgi?lan=r` |
| `spot/primary` | `generic-feed` | `https://www.spot.uz/ru/rss/` |
| `expo-position/primary` | `generic-feed` | `https://expoposition.com/feed/` |
| `atakent/primary` | `generic-structured` | `https://www.atakentexpo.kz/calendar` |
| `hunarmand/association-calls` | `generic-site` | `https://www.hunarmandlar.uz/sitemap.xml` |
| `bmca-uz/primary` | `generic-site` | `https://texworld-apparel.uz/sitemap.xml` |
| `bmca-uz/portfolio` | `generic-site` | `https://bmca.uz/sitemap.xml` |
| `bmca-uz/apparel-sourcing` | `generic-site` | `https://apparel-sourcing.uz/sitemap.xml` |
| `catexpo/primary` | `generic-site` | `https://fashionexpo.kz/Sitemap.xml` |
| `idem/primary` | `generic-site` | `https://idem.uz/sitemap.xml` |
| `ttysi/primary` | `generic-site` | `https://ttysi.uz/sitemap.xml` |
| `mrdi/primary` | `generic-site` | `https://mrdi.uz/sitemap.xml` |
| `sdtu/primary` | `generic-site` | `https://sdtu.uz/sitemap.xml` |
| `visituzbek/primary` | `generic-site` | `https://www.visituzbek.uz/sitemap.xml` |
| `eventflow/primary` | `generic-site` | `https://eventflow.uz/sitemap.xml` |
| `biexpo/primary` | `generic-site` | `https://biexpo.kg/sitemap.xml` |
| `central-asia-textile-expo/primary` | `generic-site` | `https://centralasiatextileexpo.com/sitemap.xml` |
| `olbilet/primary` | `generic-site` | `https://olbilet.uz/sitemap.xml` |
| `aral-school/primary` | `generic-site` | `https://www.aralschool.uz/sitemap.xml` |
| `ichkari/primary` | `generic-site` | `https://ichkari.com/sitemap.xml` |
| `emkp/primary` | `generic-site` | `https://www.emkp.org/sitemap.xml` |

- Seed rule: a feed channel seeds at its feed, a structured channel at its page and every site channel at the sitemap its
  host's `robots.txt` names (17 channels), because a site seeded at a page reads that one page and follows no links, while
  a sitemap seed walks article addresses, within `MaxPages` 5 a run. The registry's `seed` field carries it; a seed
  equal to the channel URL stays unset. No two kept channels share a seed.
- `idem/festival-listings` seeds at `https://idem.uz/cal/c/festival.ics`, the page's `webcal://` link read over HTTPS.
  `bmca-uz/primary`'s `robots.txt` names its sitemap with `http://`; the seed is the `https://` form, which no probe
  fetched. A sitemap lists a site's every page, not only its events, so a site channel reads as many of them as the run
  bounds allow and the tagging model decides what is relevant.
- `kultura-uz/primary`'s feed is `windows-1251`; the readers register the runtime's code-page provider once, so it
  decodes cold.
- Dropped by the registry check, which lets only an `owned` channel harvest: `acdf/bukhara-biennial-programme`,
  `namangan-region/flower-festival-programme`, `kfw/astana-edition-publisher` are `related`; they stay `planned`.
- Dropped: `glamour-avenue/primary`, because its robots.txt names a sitemap on another host (glamourboutique.uz, a
  shop), not the channel's; it stays as it was.
- Left out on purpose: the `gov.uz` channels (a Wave 2 reader), Ticketon (paused, a challenge), `afisha/calendar`
  (Afisha's reader owns that host), and the newsrooms `uzdaily`, `kabar`, `yuz`, `dunyo`, `namangan-today` and
  `tashkent-city` (whole sites; sections only, none confirmed).

### Wave 2: Telegram channels (2026-11-21)

10 channels take the `telegram-preview` route (`sources/Telegram/telegram.md`) with `mode` `harvest` and `setup`
`default`. The probe ledger of 2026-11-21 routed 14 channels to it; every one answered its `t.me/s/{username}` preview
with dated messages, and only an `owned` channel passes the registry check. One source per channel (handle
`tg:{username}`, authority 80), every channel on the shared host key `t.me`, so they queue through one lane: 10 seconds
between requests, `MaxPages` 3 a run, the newest page first.

| Channel | Pipeline seed |
|---|---|
| `uz-tourism/committee-telegram` | `https://t.me/s/uzbektourismofficial` |
| `iteca-uz/organizer-telegram` | `https://t.me/s/iteca` |
| `arts-academy/academy-telegram` | `https://t.me/s/artacademyuz` |
| `qashqadaryo-region/primary` | `https://t.me/s/kashkadaryo_travel` |
| `navoi-tourism/primary` | `https://t.me/s/navoitourism` |
| `tashkent-city/primary` | `https://t.me/s/visittashkent` |
| `goethe-uz/primary` | `https://t.me/s/goetheinstituttaschkent` |
| `twig/primary` | `https://t.me/s/twig2018` |
| `biexpo/organizer-telegram` | `https://t.me/s/biexpo4you` |
| `glamour-avenue/official-telegram` | `https://t.me/s/glamour_avenue` |

- Left as they were, because only an `owned` channel harvests and these four are `related`:
  `fergana-region/regional-tourism-updates`, `khorezm-region/regional-tourism-updates`,
  `bukhara-region/regional-tourism-updates`, `namangan-region/regional-tourism-updates`; they stay `manual`.
- Left as they were, the ledger having routed them `manual`: `hunarmand/telegram-announcements` (the preview redirects to
  the channel page, with no dated message widgets) and `caex/venue-telegram` and `human-house/gallery-telegram` (the body
  timed out).
- `glamour-avenue/official-telegram` cites the channel page; its seed is the preview page. `twig/primary` (latest dated
  message 2023-09-20) and `biexpo/organizer-telegram` (2026-11-10) are dated but quiet, so they may add little.
- Posts are classified like every other source's: the publication time is the post's, never an event date. No live
  capture ran, and `t.me/robots.txt` answered 404 on 2026-11-21, which means no rules.

### Wave 5: unranked publishers (2026-11-21)

21 channels of 21 new publishers take a generic route or the Telegram preview with `mode` `harvest` and `setup` `default`:
4 `generic-feed`, 15 `generic-site` and 2 `telegram-preview`. The source screen of 2026-11-21 (`source-screen.md`, wow-two-ws)
kept 32 of 277 unranked hosts; these are the 21 it routed to a generic reader or a Telegram channel, in its order. Each is
its own group, ranked 101 to 121, tier `P3`, with one `owned` `primary` channel, so each provisions as one source and
one catalog entry (`ocharo.{groupId}`) that starts `new` on every instance.

| Channel | Route | Pipeline seed |
|---|---|---|
| `podium-uz/primary` | `generic-site` | `https://podium.uz/sitemap.xml` |
| `tashkentdesignweek-uz/primary` | `generic-site` | `https://www.tashkentdesignweek.uz/sitemap.xml` |
| `jewelryfair-uz/primary` | `generic-site` | `https://jewelryfair.uz/wp-sitemap.xml` |
| `anhor-uz/primary` | `generic-site` | `https://anhor.uz/sitemap-index.xml` |
| `art-blog-uz/primary` | `generic-feed` | `https://art-blog.uz/feed` |
| `myticket-uz/primary` | `generic-site` | `https://myticket.uz/sitemap.xml` |
| `proart-uz/primary` | `generic-site` | `https://proart.uz/` |
| `98mag-kz/primary` | `generic-site` | `https://www.98mag.kz/sitemap_index.xml` |
| `elle-com-kz/primary` | `generic-site` | `https://elle.com.kz/sitemap.xml` |
| `esquire-kz/primary` | `generic-site` | `https://esquire.kz/sitemap.xml` |
| `weproject-media/primary` | `generic-site` | `https://weproject.media/sitemap.xml` |
| `bishkekft-com/primary` | `generic-feed` | `https://bishkekft.com/feed/` |
| `ticketon-tj/primary` | `generic-site` | `https://ticketon.tj/sitemap.xml` |
| `mffrussia-com/primary` | `generic-feed` | `https://www.mffrussia.com/blog-feed.xml` |
| `cis-fashion-ru/primary` | `generic-site` | `https://cis-fashion.ru/sitemap.xml` |
| `designoutlook-ru/primary` | `generic-site` | `https://designoutlook.ru/sitemap.xml` |
| `riamoda-ru/primary` | `generic-feed` | `https://riamoda.ru/rss.xml` |
| `exponet-ru/primary` | `generic-site` | `https://exponet.ru/exhibitions/area/cis/topics/moda/dates/future/index.en.html` |
| `styleofeurasia-com/primary` | `generic-site` | `https://www.styleofeurasia.com/` |
| `aydatuda-tg/primary` | `telegram-preview` | `https://t.me/s/aydatuda` |
| `proartworld-tg/primary` | `telegram-preview` | `https://t.me/s/proartworld` |

- Feeds: the screen read the 7 feed URLs from alternate links and fetched none. Each was fetched once on 2026-11-21 under
  the identified agent after its host's `robots.txt` (14 requests, 7 seconds apart, no redirect followed, no retry).
  4 answered 200 with dated RSS items and are the 4 feed channels: `art-blog.uz` (10 items, newest 2026-10-28),
  `bishkekft.com` (4 items, newest 2019-03-27, so it may add little), `mffrussia.com` (20, 2026-11-19) and `riamoda.ru`
  (120, 2026-11-20). 3 answered 200 with an empty channel (`jewelryfair.uz`, `proart.uz`, `styleofeurasia.com`) and took
  the site route instead.
- Site seeds: the sitemap named in the host's `robots.txt` when it is on that host, an http to https upgrade allowed
  (`cis-fashion.ru`). `anhor.uz` names two; the seed is the first, the sitemap index, and the second is a news sitemap.
  Where no sitemap is on the host, the seed is the listing page, because `robots.txt` allows it: `proart.uz` and
  `styleofeurasia.com` name none, and `exponet.ru` names one on `www.exponet.ru`, another host. A page seed reads that one page
  and follows no links. No two channels share a seed. No sitemap was fetched.
- Telegram: the usernames pass `TelegramChannels`; `t.me` was not fetched, so the channels' dated messages are unconfirmed, and
  so is `aydatuda`'s country.
- Left out: 6 `manual` keeps that need a page reader (`tatlerasia.kz`, `kassir.kz`, `kassir.kg`, `tourismboard.az`,
  `cpm-moscow.com`, `eventseye.com`) and 5 `cannot` keeps (`portalbilet.uz`, `sxodim.uz`, `namangan-tourism.uz`, `echipta.tj`,
  `moscowfashion.ru`); they have no registry group.
- `tashkentdesignweek-uz` carries the same event as the P2 group `tashkent-design-week` (`designweek.uz`); it is its own P3
  group here, with its own source.
- No capture ran. A feed read is no live capture; the channels run only after `ocharo setup` provisions them.

Other channels have a proposed method and a verification note in the registry. A new reader needs a readable public route,
stable IDs, date/eligibility evidence, scoped destinations and tests before its mode moves to `harvest`. Reference pages,
archived editions, application forms, and unverified social feeds never become fetch seeds by being linked.

`GET /api/ocharo/sources/registry` returns the complete inventory to a Harvest operator.
`POST /api/ocharo/sources/setup` with `{"groupIds":["caex"],"enableSchedules":false}` returns the stable source,
pipeline and subscription IDs. Setup makes no external fetch. Run the returned pipeline through Harvest and pull its
`wow2.event.v1` deliveries for review. The default setup provisions the five named website scopes, the 25 Wave 1
channels, the 10 Wave 2 Telegram channels and the 21 Wave 5 channels (61 pipelines); CAEx requires explicit selection. Teplo's two provider channels require `groupIds: ["teplo"]` and `includeInstagram: true`; the
provider still needs configuration and publisher access, and setup keeps its schedules disabled.

The registry describes collection. Harvest retains source observations and review candidates. Cross-publisher edition
matching and promotion to Desk remain the downstream Events review step.

Local verification used the deployment pin `d77daf6`. An isolated CAEx run read the public calendar in two HTTP
requests, including robots, and recorded nine source observations with zero failed steps. The filtered pull feed
delivered TITF, Baraka Friday and TFT Expo for review. The PostgreSQL/API fixture also proved that changing a
calendar date updates the same post ID at a higher revision, and that setup and re-setup make no public requests.
