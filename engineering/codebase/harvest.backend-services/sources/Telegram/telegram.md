# HarvestAdapters.Telegram

`TelegramSourceAdapter` declares the `telegram-preview` source and `TelegramPreviewCaptureReader` reads a public
channel's web preview, `https://t.me/s/{username}`. The package performs no network requests: register both through
Harvest's web broker (`AddHarvestWebBroker`, in `sources/Module`), which owns robots policy, pacing, transport and
access-failure reporting. No login, session, bot or proxy is involved.

## Sources

One source per channel: family `telegram`, handle `tg:{username}` (the registry's form of the public username),
posture `own_fetch`, filtered/window coverage and check policy `none`. Every channel source declares the host key
`t.me`, so all channels share one lane and one polite pace, however many are registered. The Ocharo registry seeds each
channel at `https://t.me/s/{username}` (`adapterKey` `telegram-preview`); only an `owned` channel harvests.

A pipeline's scope is its own channel's preview URL, with no query overrides or path restriction; the API rejects any
other. `maxPages` is 1–3 and a run never reads more.

## What a run reads

- The newest preview page, then `?before={smallest message id on the page}`, at most 3 pages a run, at least 10 seconds
  between requests. The reader points to the older page as a listing; the engine's page budget decides whether it is read.
- A run that spends its three pages ends `partial` with the reason `page_limit`: that is its normal end for a channel
  with more history, and it never claims the window complete.
- An empty page ends the run; so does a page that does not lie before the one asked for, and a page whose smallest
  id is 1. A page without a message history (a channel whose preview is off, a challenge page) is not a preview and
  fails; a redirect off `t.me/s/{username}` is refused and a non-HTML answer is not parsed. A challenge, block or rate
  limit pauses the lane, as for every reader; nothing retries through another route.
- `t.me/robots.txt` answered 404 on 2026-11-21, which means no rules; the source obeys `robots.txt` through the engine's
  normal fetch path like every direct reader, and the identified Harvest user agent applies.

## What a capture holds

- One capture per message block (`data-post="{username}/{id}"`). External key `{username}/{id}`; the public link, and the
  credit link on every item, is the permalink `https://t.me/{username}/{id}`.
- Publication time from the message's `time[datetime]`. It is never an event date: dates in the text are the extraction
  passes' to find, and the structured node says `eventDateStatus: not-extracted-from-text`.
- The text as plain text: line breaks kept, links kept both in the text (`label (address)`) and as the capture's links.
  Hashtag searches, mentions and non-web links are not links. Forwarded posts carry the forwarding channel's own
  message; the original sender's name travels as evidence.
- Photos and videos by reference only: a photo's image address, a video's address and its thumbnail, from the Telegram
  CDN. Nothing is downloaded or copied, a link preview's image is not the message's media, and CDN addresses may expire.
- The body is the message's own HTML slice, not the whole page.

## Revisions

The content projection holds the channel, id, text, links, the kinds of media in order and the edited marker. View
counts, reactions, the clock label and CDN addresses stay out, so a re-read of an unchanged post saves no revision and
raises no delivery event. An edited text, a changed link list or media count, or a post turning `edited` makes one
revision through the engine's normal content change. A deleted post is not detected: the preview is a moving window,
never complete, and absence from it is not a deletion.

## Limits

- A run re-reads the newest pages every time. The adapter holds no store of its own and the step seam passes it no known
  ids, so it cannot stop at an id an earlier run already saved; an unchanged post costs a read, never a revision. A cursor
  across runs would need a seam in `packages/Module`.
- The preview shows recent messages only; history past three pages is out of reach until a backfill policy exists.
- Posts in a channel that disabled its preview, or that redirect from `t.me/s/` to the channel page, are out of reach:
  the registry keeps them as `manual`.
- A parse of a fixture establishes the markup shape, not live access.
