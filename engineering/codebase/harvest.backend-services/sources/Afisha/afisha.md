# HarvestAdapters.Afisha

Afisha.uz source reader for Harvest. `AfishaSourceAdapter` declares the source's web steps;
`AfishaCaptureReader` discovers visible category/article links and reads event evidence from the public HTML.
The package references Harvest's contract and AngleSharp, and performs no network I/O itself.

The host binds these types with `AddHarvestWebBroker<AfishaSourceAdapter, AfishaCaptureReader>()`.
Afisha category pages are moving windows; they never prove complete enumeration. Discovery follows visible public
links, not Afisha's robots-disallowed `/api`, `/_next` or search endpoints. No browser script is evaluated.

Article identity is its canonical URL. Source JSON-LD and embedded event schedules establish event dates; the
article's publication date remains separate. Event projections retain ticket/participation evidence without treating
a visitor ticket as seller or designer admission. Editorial articles remain articles, and malformed or missing event
dates remain unknown. Images and registration links remain source references; the reader submits nothing.

It references Harvest's contract project in this repository and ships inside the service image.
