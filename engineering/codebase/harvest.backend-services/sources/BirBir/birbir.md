# BirBir

The SCFD reader starts at the Tashkent apartment rental category in Russian or Uzbek. It discovers the linked
ads from the initial catalog grid and matches each detail page's JSON-LD Product SKU and offer URL to its numeric
ad ID. It uses SCFD's direct, paced fetcher. The window cannot establish that an ad disappeared. Search, query
pagination, contact reveal, account sign-in and link checks are outside this first pass.

The source capabilities endpoint returns the JSON Schema for the complete `birbir.ad.v1` envelope. The same
typed record serializes each collection and import. Unavailable field values are null. Structured property labels
and values are retained in `attributes` so a later iteration can promote them to typed fields.
Historical import events can carry the earlier subset of fields.

| Field | Representation and evidence |
|---|---|
| ad ID and URL | numeric ID preserved as text and the resolved listing URL |
| title, description | listing title and published JSON-LD description, which can be an excerpt |
| price | decimal amount, ISO currency enum (`UZS` or `USD`) and the original display text |
| transaction and property | `rent` and `apartment` from the category, with the rental period unspecified |
| condition and availability | known schema.org offer codes |
| size and floors | decimal square metres, integer room count and floors, room label as published |
| location | city, country, place/address labels and a district when the location label identifies one |
| seller and images | public display name and the listing CDN image URLs |
| publication | source label, UTC timestamp when the local date and time parse, and separate offer validity date |

Text and collection limits are SCFD ingestion bounds enforced before persistence in JSONB. The title cap is 500
characters, the description 24,000, label fields 256, images 30 and attributes 40. BirBir's input limits and
complete enum domains still need verification. Values in seller prose are available for the next AI pass and stay
out of deterministic fields such as floor until the source declares them.

Reference pages reviewed on 2026-11-14: [Russian example](https://birbir.uz/ru/tashkent/cat/nedvizhimost/arenda/kvartiry/o/arenda-5-komn-kv-115-m-mirabadskiy-id-247-336408425),
[Uzbek example](https://birbir.uz/uz/toshkent/cat/kochmas-mulk/ijara/kvartiralar/o/arenda-kvartir-336658549).

## Live access

Haven setup registers `birbir-collection` as a direct site source for private SCFD collection.
The fetcher obeys robots rules and records an access challenge in the run audit. The collector and
the import source expose the same listing schema.
