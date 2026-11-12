# Instagram sources and integration

Reviewed 27 October 2026. Publisher identity and Ocharo relevance are separate from API access: no Meta credentials
were supplied, and none of these accounts' current Business Discovery eligibility has been verified.

| Priority | Publisher | Evidence and use |
|---|---|---|
| 1 | [@fwtashkent](https://www.instagram.com/fwtashkent/) | Visa Fashion Week Tashkent designer calls and runway announcements. [ELLE Uzbekistan's attributed participant call](https://elleuzbekistan.com/en/new-spring-summer-2026-season-of-visa-fashion-week-tashkent/) identifies the account. That July 2025 call is historical, not evidence of current intake. |
| 2 | [@teplofest](https://www.instagram.com/teplofest/) | [TEPLO's own site](https://teplo.world/) links the festival account and describes independent designer fairs, festivals and its Tashkent/Samarkand presence. Good for local designer/vendor opportunities. |
| 3 | [@humanhousegallery](https://www.instagram.com/humanhousegallery/) | [Human House's own site](https://human-house.com/) links the profile and describes contemporary Uzbek clothing, craft and weekly events. Broad cultural programming needs the fashion/craft filter. |

These are publisher accounts, not login/session accounts. This ranking concerns fit and directness of evidence;
there is no verified comparative posting-volume measurement. TEPLO also links @teplomarket_ for market announcements.
Uzbekistan Fashion Week's [organizer directory](https://linktr.ee/ufweek) is an additional lead, separate from Visa
Fashion Week Tashkent; do not merge the two event families.

## API route

The implementation uses **Instagram API with Facebook Login / Business Discovery**. Meta's
[official Postman collection](https://www.postman.com/meta/instagram/documentation/6yqw8pt/instagram-api?entity=request-23987686-3a63181c-e5c6-4890-96dd-e4d425bef5e0)
describes Page-linked professional accounts and excludes consumer accounts. Meta's
[official SDK](https://github.com/facebook/facebook-python-business-sdk/blob/main/facebook_business/adobjects/iguser.py)
exposes the Business Discovery field. The [Business Discovery guide](https://developers.facebook.com/docs/instagram-platform/instagram-api-with-facebook-login/business-discovery/)
returned HTTP 429 during research, so live provider verification remains an explicit acceptance step.

Before enabling, configure a Meta app with the relevant Facebook Login Instagram access, your linked professional
Instagram ID, a supported Graph version and an approved token. Confirm the current permissions and access level in
Meta's app dashboard (including applicable Instagram basic and Page read permissions, App Review/Advanced Access
when required). An Instagram Login token is not assumed interchangeable with this Facebook Login route.
Public personal accounts, arbitrary stories and a universal public Instagram feed are outside this implementation.

## Setup

1. Operator: POST `/api/ocharo/sources/setup` with `{"includeInstagram":true,"enableSchedules":false}`.
   This adds one platform source, three publisher pipelines and three filtered subscriptions. Existing settings are
   preserved. New Instagram schedules stay disabled even if the setup request enables new website schedules.
2. Inject runtime secrets/configuration; never put a token into a URL, scope, checked-in JSON or browser storage:

```text
HarvestInstagram__Enabled=true
HarvestInstagram__AccessToken=<secret from your secret store>
HarvestInstagram__ProfessionalAccountId=<your linked professional account ID>
HarvestInstagram__ApiVersion=<supported vN.0>
HarvestInstagram__AllowedUsernames__0=fwtashkent
HarvestInstagram__AllowedUsernames__1=teplofest
HarvestInstagram__AllowedUsernames__2=humanhousegallery
HarvestInstagram__PageSize=25
HarvestInstagram__MaximumPages=4
```

3. Run one manual capture per profile. Verify real API access, captions, IDs, pagination, rate limits and source
   attribution. The disabled default has been tested; no live API success is claimed.
4. In the console, select the publisher under **Pipelines**, enable its capture cadence, choose interval/cron,
   timezone and optional active hours, then **Save pipeline**. The worker must be enabled to execute it.

The global proxy/session/CAPTCHA integration switch is independent; this official API client does not consume those
providers. Tokens travel only in authorization headers to graph.facebook.com. Capture bodies whitelist media ID,
caption, timestamp, permalink, type and child IDs. API errors retain codes, not provider error text.

## Interpretation and coverage

- Captions with announcement/call evidence can become event candidates. Editorial recaps are excluded by the
  deterministic caption rule and Ocharo filter; all accepted candidates still require human review.
- Publication time is retained separately. It is never copied into an event start date. Caption dates may remain
  unclassified mentions, and image-only poster dates/fees/applications are unknown until reviewed.
- Visitor, designer/runway, vendor, exhibitor and artisan routes remain distinct; a post is not a verified open form.
  No geographic exclusion is inferred from a publisher being based in Tashkent.
- Recent-media limits, API refusal, account eligibility and unprocessed image/video text prevent complete coverage.
  Missing posts do not prove deletion. No login automation or challenge-solving flow is attached to this adapter.

Offline verification covers request shape, header-only credentials, no-call disabled/allowlist gates, pagination
isolation and bounds, rate/auth failures, response limits, content revision identity, caption classification, durable
engine ingestion and Ocharo pull delivery.
