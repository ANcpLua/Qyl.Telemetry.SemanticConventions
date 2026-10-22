#!/usr/bin/env -S uv run --script
# /// script
# dependencies = ["pyyaml"]
# ///
"""Report when an upstream ref this repository pins differs from upstream.

The registry pins are exact by design: moving inputs must not change generated
constants without a commit here. This scheduled check reports upstream movement;
it does not decide whether or when to regenerate.

Three pins, two shapes. The registry pins live in registry/manifest.yaml, the Weaver
version in Version.props:

  SemConvSchemaVersion  release tag   v{version} vs the latest release
  WeaverVersion         release tag   v{version} vs the latest release
  SemConvGenAiRef       branch SHA    commit distance from the tracked branch head

The branch pin counts only movement that can reach the generated code. The manifest pins
semantic-conventions-genai's `[model]` sub-folder and nothing else, so a run of lock-file and
tooling commits on `main` that leaves `model/` untouched is reported as current, with the
distance named; the pin is stale once a commit since it changed a file under `model/`.

A lookup that cannot prove freshness exits 2 rather than reporting "current".

CLI: check_pin_freshness.py   (exit 0 = every pin current; exit 10 = a pin differs,
     reported on stdout; exit 2 = freshness could not be determined)
"""
from __future__ import annotations

import http.client
import json
import os
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

import yaml

GITHUB_API = os.environ.get("GITHUB_API_URL", "https://api.github.com")
REPO_ROOT = Path(__file__).resolve().parents[1]
VERSION_PROPS = REPO_ROOT / "Version.props"
MANIFEST = REPO_ROOT / "registry" / "manifest.yaml"

CORE_REPO = os.environ.get("SEMCONV_CORE_UPSTREAM", "open-telemetry/semantic-conventions")
GENAI_REPO = os.environ.get("SEMCONV_GENAI_UPSTREAM", "open-telemetry/semantic-conventions-genai")
GENAI_BRANCH = os.environ.get("SEMCONV_GENAI_BRANCH", "main")
GENAI_MODEL_PREFIX = "model/"
WEAVER_REPO = os.environ.get("SEMCONV_WEAVER_UPSTREAM", "open-telemetry/weaver")

COMPARE_COMMIT_LIMIT = 10
# The compare API lists at most this many changed files; a response at the cap may be
# truncated, and a truncated file list cannot prove that `model/` is untouched.
COMPARE_FILE_LIMIT = 300
EXIT_CURRENT = 0
EXIT_UNKNOWN = 2
EXIT_STALE = 10


class FreshnessUnknown(Exception):
    """The checker could not prove whether every pin matches upstream."""


def read_pin(name: str) -> str:
    """Read a pin, honouring the same environment overrides generate.sh honours."""
    override = {
        "SemConvSchemaVersion": "SEMCONV_SCHEMA_VERSION",
        "SemConvGenAiRef": "SEMCONV_GENAI_REF",
        "WeaverVersion": "SEMCONV_WEAVER_VERSION",
    }.get(name)
    if override and os.environ.get(override):
        return os.environ[override].strip()

    if name == "WeaverVersion":
        try:
            value = ET.parse(VERSION_PROPS).getroot().findtext(".//WeaverVersion")
        except (OSError, ET.ParseError) as error:
            raise FreshnessUnknown(f"could not read {VERSION_PROPS}: {error}") from error
        if value is None or not value.strip():
            raise FreshnessUnknown(f"{VERSION_PROPS} does not define WeaverVersion")
        return value.strip()

    try:
        manifest = yaml.safe_load(MANIFEST.read_text()) or {}
    except (OSError, yaml.YAMLError) as error:
        raise FreshnessUnknown(f"could not read {MANIFEST}: {error}") from error

    dependencies = {d.get("name"): d for d in manifest.get("dependencies") or []}

    if name == "SemConvSchemaVersion":
        core = dependencies.get("core")
        if not core:
            raise FreshnessUnknown(f"{MANIFEST} has no 'core' dependency")
        schema_url = core.get("schema_url", "")
        prefix = "https://opentelemetry.io/schemas/"
        if not schema_url.startswith(prefix):
            raise FreshnessUnknown(f"{MANIFEST}: core schema_url has unexpected shape: {schema_url!r}")
        return schema_url[len(prefix):]

    genai = dependencies.get("genai")
    if not genai:
        raise FreshnessUnknown(f"{MANIFEST} has no 'genai' dependency")
    registry_path = genai.get("registry_path", "")
    if "@" not in registry_path or "[" not in registry_path:
        raise FreshnessUnknown(f"{MANIFEST}: genai registry_path has unexpected shape: {registry_path!r}")
    return registry_path.split("@", 1)[1].split("[", 1)[0]


def github_json(path: str) -> dict:
    request = urllib.request.Request(f"{GITHUB_API}/{path}")
    request.add_header("Accept", "application/vnd.github+json")
    request.add_header("X-GitHub-Api-Version", "2022-11-28")
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")
    if token:
        request.add_header("Authorization", f"Bearer {token}")

    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            payload = json.load(response)
    except urllib.error.HTTPError as error:
        detail = f"HTTP {error.code}"
        if error.code in (403, 429):
            detail += " (rate limited; set GITHUB_TOKEN)"
        elif error.code == 404:
            detail += " (renamed, deleted, or unknown ref)"
        raise FreshnessUnknown(f"{path}: {detail}") from error
    except (
        urllib.error.URLError,
        OSError,
        http.client.HTTPException,
        UnicodeDecodeError,
    ) as error:
        raise FreshnessUnknown(f"{path}: {error}") from error
    except json.JSONDecodeError as error:
        raise FreshnessUnknown(f"{path}: upstream returned malformed JSON: {error}") from error

    if not isinstance(payload, dict):
        raise FreshnessUnknown(f"{path}: upstream response was not a JSON object")
    return payload


def required_nonnegative_int(payload: dict, key: str, context: str) -> int:
    """Read a required GitHub count without turning a malformed response into zero."""
    value = payload.get(key)
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        raise FreshnessUnknown(f"{context}: response carried no valid {key}")
    return value


def check_release_pin(label: str, repo: str, pinned_version: str) -> tuple[bool, list[str]]:
    """Compare a pinned release version against the repository's latest release."""
    release = github_json(f"repos/{repo}/releases/latest")
    latest_tag = release.get("tag_name")
    if not isinstance(latest_tag, str) or not latest_tag:
        raise FreshnessUnknown(f"repos/{repo}/releases/latest: response carried no tag_name")

    pinned_tag = f"v{pinned_version}"
    if latest_tag == pinned_tag:
        return True, [f"- **{label}** current at `{pinned_tag}` ({repo})"]

    release_url = release.get("html_url")
    if not isinstance(release_url, str) or not release_url:
        release_url = f"https://github.com/{repo}/releases"
    return False, [
        f"- **{label}** pinned at `{pinned_tag}`, latest release is `{latest_tag}`",
        f"  - {release_url}",
    ]


def changed_model_files(comparison: dict, context: str) -> list[str]:
    """The files under the pinned sub-folder that the comparison range changed.

    The manifest pins `[model]`, so only these can reach the generated code. A file list
    that may be truncated, or that is missing, cannot prove the sub-folder untouched.
    """
    files = comparison.get("files")
    if not isinstance(files, list):
        raise FreshnessUnknown(f"{context}: response carried no valid files list")
    if len(files) >= COMPARE_FILE_LIMIT:
        raise FreshnessUnknown(
            f"{context}: {len(files)} changed files reach the compare API's limit; "
            f"whether {GENAI_MODEL_PREFIX} changed cannot be proven"
        )
    paths: set[str] = set()
    for entry in files:
        if not isinstance(entry, dict):
            raise FreshnessUnknown(f"{context}: response carried a malformed file entry")
        for key in ("filename", "previous_filename"):
            path = entry.get(key)
            if key == "filename" and not isinstance(path, str):
                raise FreshnessUnknown(f"{context}: response carried a file entry without a filename")
            if isinstance(path, str) and path.startswith(GENAI_MODEL_PREFIX):
                paths.add(path)
    return sorted(paths)


def check_branch_pin(label: str, repo: str, pinned_sha: str, branch: str) -> tuple[bool, list[str]]:
    """Measure how far a pinned commit sits behind the head of a tracked branch."""
    comparison = github_json(f"repos/{repo}/compare/{pinned_sha}...{branch}")
    status = comparison.get("status")
    if status not in {"identical", "ahead", "behind", "diverged"}:
        raise FreshnessUnknown(f"repos/{repo}/compare: response carried unknown status {status!r}")

    context = f"repos/{repo}/compare"
    branch_ahead_by = required_nonnegative_int(comparison, "ahead_by", context)
    branch_behind_by = required_nonnegative_int(comparison, "behind_by", context)
    compare_url = comparison.get("html_url")
    if not isinstance(compare_url, str) or not compare_url:
        compare_url = f"https://github.com/{repo}/compare/{pinned_sha}...{branch}"

    if status == "identical":
        if branch_ahead_by != 0 or branch_behind_by != 0:
            raise FreshnessUnknown(f"{context}: identical comparison carried non-zero distances")
        return True, [f"- **{label}** current at `{pinned_sha[:7]}`, the head of `{branch}` ({repo})"]

    if status == "diverged":
        if branch_ahead_by == 0 or branch_behind_by == 0:
            raise FreshnessUnknown(f"{context}: diverged comparison carried a zero distance")
        return False, [
            f"- **{label}** pinned at `{pinned_sha[:7]}`, which has **diverged** from `{branch}` "
            f"({branch_ahead_by} ahead on the branch, {branch_behind_by} only on the pin)",
            f"  - {compare_url}",
        ]

    if status == "behind":
        if branch_ahead_by != 0 or branch_behind_by == 0:
            raise FreshnessUnknown(f"{context}: behind comparison carried inconsistent distances")
        return False, [
            f"- **{label}** pinned at `{pinned_sha[:7]}`, but `{branch}` is "
            f"**{branch_behind_by} commit(s) behind the pin** ({repo})",
            f"  - {compare_url}",
            "  - The pin is not the tracked branch head; check for a force-push or an incorrect pin.",
        ]

    if branch_ahead_by == 0 or branch_behind_by != 0:
        raise FreshnessUnknown(f"{context}: ahead comparison carried inconsistent distances")

    model_files = changed_model_files(comparison, context)
    if not model_files:
        return True, [
            f"- **{label}** current at `{pinned_sha[:7]}`: `{branch}` is {branch_ahead_by} commit(s) ahead, "
            f"none of which changes a file under `{GENAI_MODEL_PREFIX}` ({repo})",
            f"  - {compare_url}",
        ]

    lines = [
        f"- **{label}** pinned at `{pinned_sha[:7]}`, **{branch_ahead_by} commit(s) behind** `{branch}` ({repo}), "
        f"{len(model_files)} file(s) under `{GENAI_MODEL_PREFIX}` changed",
        f"  - {compare_url}",
    ]
    for path in model_files:
        lines.append(f"  - `{path}`")

    commits = comparison.get("commits", [])
    if not isinstance(commits, list):
        raise FreshnessUnknown(f"{context}: response carried no valid commits list")
    for commit in commits[-COMPARE_COMMIT_LIMIT:]:
        if not isinstance(commit, dict):
            raise FreshnessUnknown(f"{context}: response carried a malformed commit")
        sha = commit.get("sha")
        metadata = commit.get("commit")
        if not isinstance(sha, str) or not isinstance(metadata, dict):
            raise FreshnessUnknown(f"{context}: response carried a malformed commit")
        message = metadata.get("message")
        if not isinstance(message, str):
            raise FreshnessUnknown(f"{context}: response carried a commit without a message")
        message_lines = message.splitlines()
        summary = message_lines[0] if message_lines and message_lines[0].strip() else "(empty commit message)"
        lines.append(f"  - `{sha[:7]}` {summary}")
    if branch_ahead_by > len(commits):
        lines.append(f"  - …{branch_ahead_by - len(commits)} further commit(s) not listed by the compare API")

    return False, lines


def main() -> int:
    report: list[str] = ["## Upstream pin freshness", ""]
    stale = False

    try:
        pins = {
            "SemConvSchemaVersion": read_pin("SemConvSchemaVersion"),
            "SemConvGenAiRef": read_pin("SemConvGenAiRef"),
            "WeaverVersion": read_pin("WeaverVersion"),
        }
        checks = (
            (check_release_pin, ("SemConvSchemaVersion", CORE_REPO, pins["SemConvSchemaVersion"])),
            (check_branch_pin, ("SemConvGenAiRef", GENAI_REPO, pins["SemConvGenAiRef"], GENAI_BRANCH)),
            (check_release_pin, ("WeaverVersion", WEAVER_REPO, pins["WeaverVersion"])),
        )
        for check, arguments in checks:
            current, lines = check(*arguments)
            stale = stale or not current
            report.extend(lines)
    except FreshnessUnknown as error:
        print("\n".join(report), flush=True)
        print(f"\nfreshness unknown: {error}", file=sys.stderr)
        return EXIT_UNKNOWN

    report.append("")
    report.append(
        "One or more pins are behind upstream. Bumping is a deliberate change: re-run "
        "`scripts/generate.sh`, review the regenerated surface, and name the delta in "
        "the commit message."
        if stale
        else "Every pin matches upstream."
    )
    print("\n".join(report))
    return EXIT_STALE if stale else EXIT_CURRENT


if __name__ == "__main__":
    sys.exit(main())
