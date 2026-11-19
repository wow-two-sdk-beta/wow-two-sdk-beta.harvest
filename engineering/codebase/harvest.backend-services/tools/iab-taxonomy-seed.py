#!/usr/bin/env python3
"""Generate the 011-taxonomy seed SQL from IAB Tech Lab's Content Taxonomy 3.1 TSV.

Run once per taxonomy release; the SQL is committed and the TSV is not:

    python3 -I tools/iab-taxonomy-seed.py <Content Taxonomy 3.1.tsv> > seed.sql

The source is https://github.com/InteractiveAdvertisingBureau/Taxonomies (Content Taxonomies/Content Taxonomy 3.1.tsv),
IAB Tech Lab Content Taxonomy 3.1, licensed CC BY 3.0. Tier-1 Events, Genres and Sensitive Topics and their subtrees are
left out: they are an entity type, media genres and a brand-safety flag, not subjects (scfd-entity-model #6.1).
"""

import argparse
import re
import sys
from pathlib import Path

EXCLUDED_TIER_ONE = {"Events", "Genres", "Sensitive Topics"}
EXPECTED_DOMAINS = 34


def slug(name):
    """Lowercase ASCII slug: apostrophes vanish, every other run of non-alphanumerics becomes one hyphen."""
    value = name.lower().replace("'", "").replace("’", "")
    value = re.sub(r"[^a-z0-9]+", "-", value).strip("-")
    if not value:
        raise ValueError(f"empty slug for {name!r}")
    return value


def read_rows(path):
    rows = []
    with path.open(encoding="utf-8-sig", newline="") as handle:
        for number, line in enumerate(handle, start=1):
            cells = [cell.strip() for cell in line.rstrip("\r\n").split("\t")]
            if number <= 2 or not cells[0]:
                continue
            identifier, parent, name = cells[0], cells[1], cells[2]
            tiers = [cell for cell in cells[3:7] if cell]
            # IAB's own Extension column marks sensitive-category rows (`SCD`); our `extension` column means a node we
            # added, so every IAB row seeds false.
            rows.append({"id": identifier, "parent": parent or None, "name": name, "tiers": tiers})
    return rows


def build_nodes(rows):
    """Keys follow the tier path, which wins over the Parent column: 3.1 files Horse Racing (497) under Sports while its
    tiers read Sports › Equine Sports, and Public Radio (376) the same way under Genres."""
    nodes, keys = [], {}
    for row in rows:
        if not row["tiers"] or row["tiers"][-1] != row["name"]:
            raise ValueError(f"row {row['id']} names {row['name']!r} but its tiers end with {row['tiers'][-1:]!r}")
        if row["tiers"][0] in EXCLUDED_TIER_ONE:
            continue
        path = [slug(tier) for tier in row["tiers"]]
        key = "/".join(path)
        if key in keys:
            raise ValueError(f"key {key!r} repeats for {row['id']} and {keys[key]}")
        keys[key] = row["id"]
        nodes.append({"key": key, "iab_id": row["id"], "parent_key": "/".join(path[:-1]) or None, "tier": len(path),
                      "label": row["name"], "extension": False})
    domains = [node for node in nodes if node["tier"] == 1]
    if len(domains) != EXPECTED_DOMAINS:
        raise ValueError(f"expected {EXPECTED_DOMAINS} domains, found {len(domains)}")
    for node in nodes:
        if node["parent_key"] is not None and node["parent_key"] not in keys:
            raise ValueError(f"node {node['key']} has no parent row")
    return sorted(nodes, key=lambda node: (node["tier"], node["key"]))


def literal(value):
    return "NULL" if value is None else "'" + str(value).replace("'", "''") + "'"


def render(nodes):
    lines = ["INSERT INTO harvest_taxonomy_nodes(key, iab_id, parent_key, tier, label, extension, active) VALUES"]
    values = [f"    ({literal(node['key'])}, {literal(node['iab_id'])}, {literal(node['parent_key'])}, {node['tier']}, "
              f"{literal(node['label'])}, {'true' if node['extension'] else 'false'}, true)" for node in nodes]
    lines.append(",\n".join(values))
    lines.append("ON CONFLICT (key) DO NOTHING;")
    return "\n".join(lines) + "\n"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("tsv", type=Path, help="IAB Content Taxonomy 3.1 TSV")
    args = parser.parse_args(argv)
    nodes = build_nodes(read_rows(args.tsv))
    sys.stdout.write(f"-- {len(nodes)} nodes: {sum(1 for node in nodes if node['tier'] == 1)} domains and their IAB descendants.\n")
    sys.stdout.write(render(nodes))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
