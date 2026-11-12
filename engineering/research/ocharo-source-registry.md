# Ocharo source registry and collection review

*Last updated: 2026-11-09*

The [registry](../codebase/harvest-adapters.backend-services/packages/Module/Ocharo/ocharo-event-sources.json)
is the ranked, reviewed collection inventory: 100 publishers and 189 channels. It keeps priority separate from
whether a collector can run. Research citations, past editions and non-selected links stay in the business
event index. A publisher can have several channels; shared social or government hosting does not make them the same publisher.

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

Other channels have a proposed method and a verification note in the registry. A new reader needs a readable public route,
stable IDs, date/eligibility evidence, scoped destinations and tests before its mode moves to `harvest`. Reference pages,
archived editions, application forms, and unverified social feeds never become fetch seeds by being linked.

`GET /api/ocharo/sources/registry` returns the complete inventory to a Harvest operator.
`POST /api/ocharo/sources/setup` with `{"groupIds":["caex"],"enableSchedules":false}` returns the stable source,
pipeline and subscription IDs. Setup makes no external fetch. Run the returned pipeline through Harvest and pull its
`wow2.event.v1` deliveries for review. Existing defaults continue to provision five website scopes; CAEx requires
explicit selection. Teplo's two provider channels require `groupIds: ["teplo"]` and `includeInstagram: true`; the
provider still needs configuration and publisher access, and setup keeps its schedules disabled.

The registry describes collection. Harvest retains source observations and review candidates. Cross-publisher edition
matching and promotion to Desk remain the downstream Events review step.

Local verification used the deployment pin `d77daf6`. An isolated CAEx run read the public calendar in two HTTP
requests, including robots, and recorded nine source observations with zero failed steps. The filtered pull feed
delivered TITF, Baraka Friday and TFT Expo for review. The PostgreSQL/API fixture also proved that changing a
calendar date updates the same post ID at a higher revision, and that setup and re-setup make no public requests.
