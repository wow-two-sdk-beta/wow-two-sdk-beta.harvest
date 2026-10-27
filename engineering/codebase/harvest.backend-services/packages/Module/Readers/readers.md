# Readers/

Generic family readers parse fetched bytes without I/O. Named-source brokers compose the same readers.

| Implements (Abstractions) | Readers |
|---|---|
| `Readers.ICaptureReader` → `CaptureReadResult` (`Capture`, `DiscoveredUrl`) | JSON-LD `Event`, RSS, Atom, ICS, sitemap/index, OpenGraph metadata, plain pages with links and media |

- A reader parses a `FetchedDocument`; it fetches nothing.
- `Capture.ContentProjection` excludes metrics, ads, session and CDN tokens, so the content hash moves only on content.
- Generic adapters over these readers implement `Steps.ISourceAdapter` for the `structured`, `site`, `feed`, `page` and
  `link` families.

## Composition

`AddHarvestReaders()` registers parsers in order: sitemap, feed, ICS, JSON-LD event, plain page. It also registers
`generic-site`, `generic-structured`, `generic-page`, `generic-feed`, and `generic-link`, each with `enumerate`,
`detail`, and `check` step brokers. A host separately registers `IPageFetcher`; built-in steps never fetch through an
unpaced `HttpClient`.

- site capture starts at `scope.url`, or `https://{handle}/sitemap.xml`; other capture starts at `scope.url`, or the host root
- child sitemap entries remain enumeration work; URL-set entries become same-host detail work
- every fetch outcome reaches the engine; only direct 404/410 responses emit strong negative evidence
- cached 304 bodies are reparsed, so a crash after the HTTP validator was saved cannot lose a capture
- an empty JavaScript shell returns `renderRequired`; rendering remains an optional, unbound capability

## Format boundaries

- XML disables external entities and DTDs and bounds the parsed document size
- RSS/Atom are moving windows and never claim enumeration completeness
- ICS captures each master or explicitly supplied recurrence instance; recurrence rules remain data with a warning,
  rather than synthesized occurrences. Floating times retain `TZID`; all-day `DTEND` keeps its exclusive calendar boundary
- sitemap `lastmod` is retained as source evidence and never used here to skip a fetch
- OpenGraph metadata is read from the supplied page; oEmbed fetching is not implemented
- feed-local IDs remain source-local, so a short GUID cannot merge unrelated feeds
