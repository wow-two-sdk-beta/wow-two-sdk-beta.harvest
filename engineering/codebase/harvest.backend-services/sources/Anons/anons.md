# HarvestAdapters.Anons

`AnonsSourceAdapter` declares the `anons-uz` source and `AnonsCaptureReader` reads
ANONS.uz's public HTML. The package performs no network requests. Register both
through Harvest's web broker, which owns robots policy, pacing, transport and
access-failure reporting.

Discovery follows event links, calendar links and public fragments inside a section
headed Events, События or Tadbirlar, actually present in the received HTML. Lists are moving windows and never claim archive
completeness. The reader does not construct pagination URLs, call search/profile
routes, or execute scripts.

Detail capture reads Event JSON-LD from `@graph`, the embedded Editor.js article
blocks and the visible date, time, location and visitor-price fields. Publication
time stays separate from event dates. Unknown years, time zones, participation
routes and vendor fees remain unknown. Raw date and visitor-price evidence travels
with the structured Event under `anons`.

It references Harvest's contract project in this repository and ships inside the service image.
