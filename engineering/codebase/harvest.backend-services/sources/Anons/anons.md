# HarvestAdapters.Anons

`AnonsSourceAdapter` declares the `anons-uz` source and `AnonsCaptureReader` reads
ANONS.uz's public HTML. The package performs no network requests. Register both
through Harvest's web broker, which owns robots policy, pacing, transport and
access-failure reporting.

Discovery stays in the listing's locale and returns its event links first, then calendar-day
links (`?date=YYYY-MM-DD`) and public fragments inside a section headed Events, События or Tadbirlar, actually present
in the received HTML; the engine admits them in that order within the run's page budget. Tag and day filters, the
other locale's list and the home page repeat the same window and are not followed from an events listing. Lists are
moving windows and never claim archive completeness. The reader does not construct pagination URLs, call
search/profile routes, or execute scripts.

Detail capture reads Event JSON-LD from `@graph`, the embedded Editor.js article
blocks and the visible date, time, location and visitor-price fields. Publication
time stays separate from event dates. When the Event's start time, read in Tashkent time, differs from the printed
time field, the event keeps only its days and the capture warns `event_time_conflict`. Unknown years, time zones, participation
routes and vendor fees remain unknown. Raw date and visitor-price evidence travels
with the structured Event under `anons`.

It references Harvest's contract project in this repository and ships inside the service image.
