# OLX Uzbekistan egress

*Reviewed 2026-11-05.*

GeoNode and Decodo are the first two candidates. Haven added Decodo to its browser extractor in
`69001efab2091c7e12b24c13474f535527dd2e08` and registered Decodo and GeoNode in
`4cd49e62eb6c51fae13fbdcd77a2ec20dd36b6af`. The latter used GeoNode for listing details.
That history establishes which integrations existed. A current endpoint and credential still need to be supplied.

| Order | Option | Fit for Harvest | Advertised terms |
|---|---|---|---|
| 1 | GeoNode | Existing Haven detail-fetch precedent and an advertised Uzbek residential pool. First bounded managed trial. | 10 GB for $7.92 per month. [Location](https://geonode.com/proxies-by-location/uzbekistan-proxy) · [Pricing](https://geonode.com/products/residential-proxies) |
| 2 | Decodo | Existing Haven browser precedent. Its location list includes Uzbekistan. Pay-as-you-go suits a small comparison. | $4/GB pay-as-you-go or $11.25 for 3 GB monthly, plus VAT. [Locations](https://decodo.com/proxies/list) · [Pricing](https://decodo.com/proxies/residential-proxies) |
| 3 | Pro-Data VPS | A managed-by-us fixed Uzbek exit. Suitable for predictable network identity if a bounded acceptance run succeeds. | Monthly VPS rental and an operated proxy. [Uzbekistan VPS](https://www.pro-data.tech/en/virtual-server) |
| 4 | Oxylabs | Second managed residential pool with an Uzbekistan-specific offering. | Confirm Uzbekistan eligibility and price on the selected plan. [Location](https://oxylabs.io/location-proxy/uzbekistan) |
| 5 | Bright Data | Uzbekistan coverage across several network types. Useful if a later requirement needs a particular network type. | Bandwidth or IP pricing depends on network and plan. [Location and plans](https://brightdata.com/locations/uz) |

The order reflects the prior integrations, local egress control and cost for an initial run. The provider pages
describe their offers. OLX availability and refusal rates need a direct comparison on the same bounded search.

## Code ownership

- The backend SDK owns proxy connection guards, fixed endpoint profiles and the HTTPS lease-broker client.
- Harvest chooses a profile for `olx.uz` and `www.olx.uz` over HTTPS. Set
  `HarvestIntegrations:Enabled=true` and choose `HarvestIntegrations:OlxProxyProfile` from
  `HarvestIntegrations:ProxyProfilesJson`. GeoNode, Decodo, Oxylabs and Bright Data use their standard proxy
  endpoints. For Pro-Data, use the endpoint of an authenticated proxy operated on the VPS.
- Harvest owns pacing, robots policy and refusal handling. Haven Supply pulls delivered listings and owns its
  classification and triage.

## Acceptance

Use one configured endpoint with an approved credential. Run the same limited OLX search used in the direct
probe. Record the egress region, request count, elapsed time, outcome and delivery count. If OLX refuses the request,
record the refusal and observe the configured pause. Compare with the existing direct result and pull the delivery
with the Haven service client before enabling schedules. [Bounded OLX probe](olx-source-review.md)
