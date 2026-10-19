#!/usr/bin/env python3
"""Gate owned --future defects; retain explicitly classified dependency/tooling warnings."""
import collections
import json
import os
from pathlib import Path
import shlex
import subprocess
import sys


DEPENDENCY_WARNINGS = {
    "InvalidAttributeWarning", "MissingManifestFileFormat", "MissingRequirementLevelWarning",
    "UnstableFileFormat",
}


def classify(row, root):
    error = row.get("error", {}).get("FailToResolveDefinition", {})
    if len(error) != 1:
        return "defect"
    kind, details = next(iter(error.items()))
    location = details.get("path_or_url", details.get("provenance", details.get("path", "")))
    if not isinstance(location, str) or not location:
        return "defect"
    path = Path(location)
    if not path.is_absolute():
        path = root / path
    owned = path.resolve().is_relative_to((root / "registry").resolve())
    # Weaver 0.28 marks definition/2 experimental even when its content is valid.
    if kind == "UnstableFileFormat" and details.get("file_format") == "definition/2":
        return "format_warning" if owned else "dependency_warning"
    if not owned and kind in DEPENDENCY_WARNINGS:
        return "dependency_warning"
    return "defect"


def diagnostics(output):
    decoder = json.JSONDecoder()
    for index, character in enumerate(output):
        if character != "[":
            continue
        try:
            rows, _ = decoder.raw_decode(output[index:])
        except json.JSONDecodeError:
            continue
        if isinstance(rows, list) and all(isinstance(row, dict) and "diagnostic" in row for row in rows):
            return rows
    raise ValueError("Weaver returned no diagnostic JSON array")


def main():
    root = Path(__file__).resolve().parents[1]
    command = shlex.split(os.environ.get("WEAVER", "weaver"))
    run = subprocess.run(command + ["registry", "check", "-r", "registry", "--v2", "--future",
                                   "-p", "registry/policies-v2", "--diagnostic-format", "json"],
                         cwd=root, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    output = root / "artifacts/logs/future.log"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(run.stdout)
    if run.returncode not in (0, 1):
        print(run.stdout, file=sys.stderr)
        return 1
    try:
        rows = diagnostics(run.stdout) if run.returncode else []
    except ValueError:
        print(run.stdout, file=sys.stderr)
        return 1
    grouped = collections.defaultdict(list)
    for row in rows:
        grouped[classify(row, root)].append(row)
    report = output.with_suffix(".json")
    report.write_text(json.dumps(dict(grouped), indent=2) + "\n")
    for category, findings in sorted(grouped.items()):
        print(f"{category}: {len(findings)} (details: {report.relative_to(root)})")
    for row in grouped["defect"]:
        print(row["diagnostic"]["message"], file=sys.stderr)
    if grouped["defect"] or (run.returncode and not rows):
        return 1
    print("OK: no owned strict-registry defects; dependency and experimental-format findings reported separately.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
