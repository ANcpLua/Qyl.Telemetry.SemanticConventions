#!/usr/bin/env python3
"""Export qyl and vendor definitions at every stability, referencing upstream keys."""

from __future__ import annotations

import argparse
from datetime import date
import json
from pathlib import Path
import re

import yaml


def inline(value: object) -> str:
    return str(value).replace("\n", " ").replace("|", "\\|")


def type_name(value: object) -> str:
    if isinstance(value, dict):
        return "enum(" + ", ".join(str(m["value"]) for m in value.get("members", [])) + ")"
    return str(value)


def export(registry: Path, output: Path) -> None:
    documents = [(p, yaml.safe_load(p.read_text()) or {}) for p in sorted(registry.rglob("*.yaml")) if p.name != "manifest.yaml"]
    own_count = sum("id" in a for _, d in documents for g in d.get("groups", []) for a in g.get("attributes", []))
    lines = [
        "# qyl semantic conventions (all stabilities incl. development/experimental)", "",
        f"Source: {registry} (qyl + vendor registries; upstream OTel core and GenAI definitions are referenced, not duplicated).",
        f"Generated {date.today().isoformat()}. Own attribute definitions: {own_count}.", "",
        "The Anthropic entries preserve native keys and distinguish log events, span events, spans, metrics, and instrumentation scopes. "
        "Development entries include beta, gated, and retained compatibility paths. Source presence does not establish that an event was delivered.", "",
        "The exact producer versions and source fingerprints appear in each vendor provenance section. "
        "The Chrome extension contains span instrumentation but no standalone collector exporter was found; its product-analytics endpoint is a different transport. "
        "Dynamic names are documented as patterns rather than invented finite event lists.", "",
        "Regenerate with `uv run --no-project --with pyyaml python3 scripts/export-registry-reference.py --output <file>`.", "",
        "## Contents", "",
    ]
    for path, _ in documents:
        title = str(path.relative_to(registry))
        slug = re.sub(r"[^\w\s-]", "", title.lower()).replace(" ", "-")
        lines.append(f"- [{title}](#{slug})")
    extensions = registry.parent / "docs/browser-extension-telemetry.md"
    if extensions.exists():
        lines.append("- [Browser extension telemetry coverage](#browser-extension-telemetry-coverage)")
    for path, document in documents:
        lines.extend(["", f"## {path.relative_to(registry)}", ""])
        for group in document.get("groups", []):
            lines.extend([f"### {group['id']} ({group['type']}, {group.get('stability', '—')})", "", group.get("brief", ""), ""])
            if group.get("deprecated"):
                lines.extend(["Deprecated: " + inline(group["deprecated"]), ""])
            for key in ("name", "metric_name", "span_kind", "instrument", "unit", "extends"):
                if key in group:
                    lines.append(f"- {key}: `{group[key]}`")
            if group.get("note"):
                lines.extend(["", group["note"], ""])
            for attribute in group.get("attributes", []):
                if "id" in attribute:
                    lines.append(f"- **`{attribute['id']}`** — {type_name(attribute['type'])}, {attribute.get('stability', '—')}")
                    lines.append("  " + inline(attribute.get("brief", "")))
                    if attribute.get("note"):
                        lines.append("  Evidence: " + inline(attribute["note"]))
                    if attribute.get("deprecated"):
                        lines.append("  Deprecated: " + inline(attribute["deprecated"]))
                else:
                    lines.append(f"- ref `{attribute['ref']}` ({inline(attribute.get('requirement_level', ''))})")
            qyl = group.get("annotations", {}).get("qyl", {})
            if qyl.get("vendor"):
                vendor = qyl["vendor"]
                lines.extend(["", "Vendor provenance:", ""])
                for key in ("library", "version", "repository", "ref", "license", "artifact_sha256"):
                    if key in vendor:
                        lines.append(f"- {key}: {inline(vendor[key])}")
                for scope in vendor.get("activity_sources", []):
                    lines.append(f"- Scope `{scope['name']}`: {inline(scope['note'])}")
                if vendor.get("dynamic_conventions"):
                    lines.extend(["", "Dynamic naming / forwarding paths:", ""])
                    for item in vendor["dynamic_conventions"]:
                        lines.append("- " + inline(json.dumps(item, ensure_ascii=False)))
            if qyl.get("spans"):
                lines.extend(["", "Span naming chains:", ""])
                for name, policy in qyl["spans"].items():
                    lines.append(f"- `{name}`: " + ", ".join(f"`{v}`" for v in policy.get("span_name", {}).get("templates", [])))
            lines.append("")
    if extensions.exists():
        lines.extend(["", "## Browser extension telemetry coverage", ""])
        lines.extend("#" + line if line.startswith("#") else line for line in extensions.read_text().splitlines()[1:])
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text("\n".join(lines).rstrip() + "\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    arguments = parser.parse_args()
    export(Path(__file__).resolve().parents[1] / "registry", arguments.output)
