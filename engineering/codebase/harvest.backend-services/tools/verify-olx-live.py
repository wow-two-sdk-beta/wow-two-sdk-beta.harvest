#!/usr/bin/env python3
"""Run a small, manual OLX pipeline through a local Harvest source host and save the evidence."""

import argparse
import json
import os
import ssl
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path



class NoRedirect(urllib.request.HTTPRedirectHandler):
    """Keep operator credentials on the explicitly selected local API origin."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def create_requester(api, key, ca_file=None):
    context = ssl.create_default_context(cafile=str(ca_file) if ca_file else None)
    opener = urllib.request.build_opener(urllib.request.HTTPSHandler(context=context), NoRedirect())

    def request(path, body=None):
        data = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(api.rstrip("/") + path, data=data,
                                     headers={"X-Api-Key": key, "Content-Type": "application/json"})
        try:
            with opener.open(req, timeout=20) as response:
                return json.load(response)["data"]
        except urllib.error.HTTPError as error:
            # Do not echo arbitrary response bodies or forward the operator key through redirects.
            error.close()
            raise RuntimeError(f"Harvest returned HTTP {error.code} for {path}.") from error

    return request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api", default="https://localhost:8400")
    parser.add_argument("--ca-file", type=Path, help="Trusted development CA/certificate PEM for the HTTPS preview")
    parser.add_argument("--url", required=True, help="Public OLX category/search URL, including the saved filter")
    parser.add_argument("--tenant", default="olx-verification")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--timeout", type=int, default=180)
    args = parser.parse_args()
    api = urllib.parse.urlsplit(args.api)
    source_url = urllib.parse.urlsplit(args.url)
    if api.scheme not in {"http", "https"} or api.hostname not in {"localhost", "127.0.0.1", "::1"} \
            or api.username or api.password or api.path not in {"", "/"} or api.query or api.fragment:
        parser.error("This verification script targets a local Harvest preview.")
    if source_url.scheme != "https" or source_url.hostname not in {"olx.uz", "www.olx.uz"} or source_url.username or source_url.password:
        parser.error("Use a public HTTPS OLX Uzbekistan URL.")
    key = os.environ.get("HARVEST_API_KEY")
    if not key:
        parser.error("Set HARVEST_API_KEY to the local operator key; it is never saved in the evidence file.")
    if args.timeout <= 0:
        parser.error("--timeout must be positive.")
    target = urllib.parse.urlunsplit(source_url._replace(fragment=""))

    request = create_requester(args.api, key, args.ca_file)

    tenant = next((row for row in request("/api/tenants") if row["slug"] == args.tenant), None)
    if tenant is None:
        tenant = request("/api/tenants", {"slug": args.tenant, "name": "OLX verification"})
    container = next((row for row in request("/api/containers?tenantId=" + tenant["id"])
                      if row.get("externalRef") == "olx-verification"), None)
    if container is None:
        container = request("/api/containers", {"kind": "private", "tenantId": tenant["id"],
                                                "externalRef": "olx-verification", "embeddingsEnabled": False})
    source = next((row for row in request("/api/sources?containerId=" + container["id"])
                   if row["handle"] == source_url.hostname and row["family"] == "site"), None)
    if source is None:
        source = request("/api/sources", {
            "containerId": container["id"], "family": "site", "handle": source_url.hostname,
            "coverage": "filtered", "completeness": "window", "posture": "ownFetch", "permission": "open",
            "aiAllowed": False, "poolShareable": False, "robotsPolicy": "obey", "checkPolicy": "web-direct",
        })
    if source["aiAllowed"] or source["poolShareable"] or source["robotsPolicy"] != "obey":
        raise RuntimeError("Existing source settings differ from the isolated verification setup.")
    pipeline = next((row for row in request("/api/pipelines?containerId=" + container["id"])
                     if row["sourceId"] == source["id"] and row["scope"].get("url") == target), None)
    if pipeline is None:
        pipeline = request("/api/pipelines", {
            "containerId": container["id"], "sourceId": source["id"], "name": "OLX · Tashkent · verification",
            "scope": {"url": target, "parameters": {}}, "schedules": [], "ai": "off",
            "limits": {"maxPages": 3, "maxItems": 2, "maxRequests": 8, "maxBytes": 12582912,
                       "maxDuration": "PT2M", "maxProbes": 2},
        })
    if any(schedule["enabled"] for schedule in pipeline["schedules"]) or pipeline["ai"] != "off":
        raise RuntimeError("Verification requires manual schedules and AI off.")
    if pipeline["limits"].get("maxItems") != 2 or pipeline["limits"].get("maxPages") != 3:
        raise RuntimeError("Existing pipeline limits differ; choose a new verification tenant.")
    subscription = next((row for row in request("/api/subscriptions?tenantId=" + tenant["id"])
                         if row["pipelineId"] == pipeline["id"] and row.get("filterKey") is None), None)
    if subscription is None:
        subscription = request("/api/subscriptions", {"tenantId": tenant["id"], "containerId": container["id"],
                                                       "pipelineId": pipeline["id"], "schema": "wow2.post.v1",
                                                       "context": {"verification": "olx"}})
    run = request("/api/runs", {"pipelineId": pipeline["id"], "kind": "capture"})
    deadline = time.monotonic() + args.timeout
    while run["status"] in {"queued", "running"} and time.monotonic() < deadline:
        time.sleep(0.5)
        run = request("/api/runs/" + run["id"])
    posts = request("/api/posts?containerId=" + container["id"] + "&limit=100")
    current = [post for post in posts if any(link.get("lastCheckRunId") == run["id"] for link in post["links"])]
    events = request("/api/subscriptions/" + subscription["id"] + "/events")
    evidence = {"sourceUrl": target, "tenantId": tenant["id"], "containerId": container["id"],
                "sourceId": source["id"], "pipelineId": pipeline["id"], "subscriptionId": subscription["id"],
                "run": run, "posts": current, "delivery": events, "fullCoverage": False}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n")
    if run["status"] not in {"completed", "partial"} or not 1 <= len(current) <= 2:
        raise RuntimeError(f"No verified bounded capture: status={run['status']}; inspect {args.output}")
    if any(post["base"].get("kind") != "listing" for post in current):
        raise RuntimeError("The source did not produce mapped listing records.")
    if any(link.get("sourceFields", {}).get("schema") != "olx.ad.v1" for post in current for link in post["links"]):
        raise RuntimeError("An initial OLX field snapshot is missing.")
    if not any(post["base"].get("prices") for post in current):
        raise RuntimeError("No published price was mapped; inspect the source snapshots.")
    if not events["data"]:
        raise RuntimeError("No durable delivery event was available.")
    print(json.dumps({"runId": run["id"], "status": run["status"], "reason": run.get("completionReason"),
                      "captured": run["captured"], "unchanged": run["unchanged"], "failed": run["failed"],
                      "requests": run["requests"], "workPages": run["pages"], "records": len(current),
                      "deliveryEvents": len(events["data"]), "evidence": str(args.output)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
