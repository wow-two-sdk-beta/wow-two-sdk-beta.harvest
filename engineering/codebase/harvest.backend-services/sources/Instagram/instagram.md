# Harvest Instagram

Opt-in source for Meta's Instagram Business Discovery API (Facebook Login). Collects recent media from explicitly
allowlisted professional publishers into the Harvest engine. Defaults are disabled and make no provider calls.

```csharp
services.AddHarvestInstagram().Bind(configuration.GetSection("HarvestInstagram"));
```

Runtime settings:

| Setting | Meaning |
|---|---|
| `Enabled` | Explicit activation, default `false` |
| `AccessToken` | Approved Facebook Login token; secret store/environment only |
| `ProfessionalAccountId` | Your Page-linked professional Instagram account ID |
| `ApiVersion` | A currently supported version in `vN.0` form; deliberately has no default |
| `AllowedUsernames` | Exact publisher allowlist; empty permits no accounts |
| `PageSize` | 1–50, default 25 |
| `MaximumPages` | Recent pages per run, 1–20, default 4 |
| `MaximumResponseBytes` | 1 KiB–8 MiB, default 2,000,000 |
| `Timeout` | Request deadline, default 30 seconds, maximum one minute |

Register family `instagram`, handle `www.instagram.com`, posture `official`, filtered/window coverage and check
policy `none`. Each publisher is a separate pipeline with scope URL `https://www.instagram.com/USERNAME/`.
Capture/discover/sweep are supported; refresh, deletion checking, stories, hashtags and personal accounts are not.
Ocharo setup can add its reviewed profiles with `{"includeInstagram":true,"enableSchedules":false}`.

The client uses a fixed guarded HTTPS origin, authorization headers, bounded JSON, and no automatic retries.
Pagination reads cursors and rebuilds the request; provider `paging.next` URLs are never followed or stored.
Frontier URLs containing `__harvest_cursor` are internal same-origin work identifiers, not observed Instagram links.
Credentials, API request URLs, paging URLs and engagement counters are excluded from durable capture bodies.
Media identity and caption edits drive revisions; rotating CDN signatures and counters do not.

Missing credentials and ineligible publishers produce explicit outcomes. Empty or bounded listings never establish
deletion. Caption announcements enter Ocharo's review stream with unknown event dates; publication timestamps remain
publication timestamps. Images/videos are referenced, not downloaded or read with OCR. CDN URLs can expire.

Transport metadata keeps the actual Graph request URL and HTTP status for provenance; that URL contains no access
token. Public source links always use the canonical Instagram permalink.

See the repository's [account review and setup](../../../../research/instagram-source-review.md).
