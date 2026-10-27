# Readers/

The generic family readers — iteration 4. Public, so a self-hosted Harvest reads something on day one; named-source
brokers compose them.

| Implements (Abstractions) | Readers |
|---|---|
| `Readers.ICaptureReader` → `CaptureReadResult` (`Capture`, `DiscoveredUrl`) | JSON-LD `Event`, RSS, Atom, ICS, sitemap (`lastmod` trust per source), link cards (OpenGraph, oEmbed), plain pages with links and media |

- A reader parses a `FetchedDocument`; it fetches nothing.
- `Capture.ContentProjection` excludes metrics, ads, session and CDN tokens, so the content hash moves only on content.
- Generic adapters over these readers implement `Steps.ISourceAdapter` for the `structured`, `site`, `feed`, `page` and
  `link` families.
