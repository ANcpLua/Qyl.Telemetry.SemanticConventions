#!/usr/bin/env bash
# Regenerates every committed generated file from registry/ with Weaver. There is no
# intermediate JSON and no compile-time generator: Weaver's materialized v2 registry goes
# straight into the Jinja templates under templates/, and the output is committed.
# scripts/check-generated.sh runs this and fails on `git diff`.
#
# Pins live in exactly one place, registry/manifest.yaml. Version.props keeps WeaverVersion,
# and this guard refuses any other binary; mise.toml installs the binary, so a Weaver bump
# moves both files.
set -euo pipefail

# Quiet unless --verbose or QYL_VERBOSE=1: scripts/quiet.sh prints PASS or FAIL, the failing
# tail and the path of the whole Weaver output, artifacts/logs/generate.log.
if [[ ${QYL_VERBOSE:-0} != 1 && ${1:-} != --verbose ]]; then
  exec "$(dirname "${BASH_SOURCE[0]}")/quiet.sh" generate -- "${BASH_SOURCE[0]}"
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
cd "${repo_root}"

# Plain python3 has only the standard library. uv supplies PyYAML from its cache.
python_yaml() { uv run --quiet --no-project --with pyyaml python3 "$@"; }

WEAVER="${WEAVER:-weaver}"
read -r -a WEAVER_CMD <<< "${WEAVER}"

expected_weaver_version="$(python3 - Version.props <<'PY'
import sys
import xml.etree.ElementTree as ET

value = ET.parse(sys.argv[1]).getroot().findtext(".//WeaverVersion")
if not value or not value.strip():
    raise SystemExit("error: Version.props does not define WeaverVersion")
print(value.strip())
PY
)"

actual_weaver_version="$("${WEAVER_CMD[@]}" --version | awk '{print $2}')"
if [[ "${actual_weaver_version}" != "${expected_weaver_version}" ]]; then
  echo "error: Weaver ${expected_weaver_version} is required; found ${actual_weaver_version}" >&2
  echo "set WEAVER to the pinned binary or intentionally update WeaverVersion in Version.props" >&2
  exit 1
fi

# The registry's schema URL carries the package version: https://qyl.at/schemas/<VersionPrefix>.
# It is a constant in the generated code and the URL every consumer's telemetry names, so a
# release whose manifest still says the previous version ships a lie. Refuse to generate one.
version_prefix="$(python3 - Directory.Build.props <<'PYVP'
import sys
import xml.etree.ElementTree as ET
value = ET.parse(sys.argv[1]).getroot().findtext(".//VersionPrefix")
if not value or not value.strip():
    raise SystemExit("error: Directory.Build.props does not define VersionPrefix")
print(value.strip())
PYVP
)"
manifest_schema_url="$(python_yaml - registry/manifest.yaml <<'PYSU'
import sys
import yaml
print((yaml.safe_load(open(sys.argv[1])) or {}).get("schema_url", ""))
PYSU
)"
if [[ "${manifest_schema_url}" != "https://qyl.at/schemas/${version_prefix}" ]]; then
  echo "error: registry/manifest.yaml schema_url is '${manifest_schema_url}', but VersionPrefix is ${version_prefix};" >&2
  echo "       expected https://qyl.at/schemas/${version_prefix} (the URL is versioned with the package)" >&2
  exit 1
fi
"${script_dir}/fetch-core.sh"

core_ref="$(cat .build/core-ref.txt)"
core_commit="$(cat .build/core-commit.txt)"

# The three schema URLs are registry facts: Weaver's materialized v2 registry carries them and
# the template filters read them off it. The git ref the genai dependency is pinned to is not
# part of that registry, so it is read out of the manifest here and handed over as a parameter.
genai_commit="$(python_yaml - registry/manifest.yaml <<'PY'
import sys
import yaml

manifest = yaml.safe_load(open(sys.argv[1])) or {}
by_name = {d["name"]: d for d in manifest.get("dependencies") or []}
print(by_name["genai"]["registry_path"].split("@", 1)[1].split("[", 1)[0])
PY
)"

# Weaver's materialized v2 registry flattens `registry.*` groups into the catalog and drops
# their group annotations, so the two lists the templates need — the scope names qyl
# constructs and the ActivitySource names of the pinned vendor libraries — are read out of the
# same YAML here and handed to the templates as parameters. registry/ stays the only source.
scope_names="$(python_yaml - registry/qyl/names.yaml <<'PY'
import sys
import yaml

for group in (yaml.safe_load(open(sys.argv[1])) or {}).get("groups") or []:
    names = ((group.get("annotations") or {}).get("qyl") or {}).get("scope_names")
    if names:
        print(",".join(sorted(set(names))))
        break
else:
    raise SystemExit("error: registry/qyl/names.yaml declares no annotations.qyl.scope_names")
PY
)"

vendor_activity_sources="$(python_yaml - <<'PY'
import glob
import yaml

names = set()
for path in sorted(glob.glob("registry/vendor/*.yaml")):
    for group in (yaml.safe_load(open(path)) or {}).get("groups") or []:
        vendor = ((group.get("annotations") or {}).get("qyl") or {}).get("vendor")
        if not vendor:
            continue
        for source in vendor.get("activity_sources") or []:
            names.add(source["name"])
if not names:
    raise SystemExit("error: registry/vendor/*.yaml declare no activity_sources")
print(",".join(sorted(names)))
PY
)"

# qyl's policy over the registry (registry/qyl/policy.yaml): the persistence rule, the
# end-of-operation attributes and the span-name chains and starters. Its annotations ride on
# non-registry groups, which the materialized v2 registry drops, so they are read here into one
# JSON parameter file every Weaver target reads as params.policy. Keeping the policy off
# argv avoids Linux's per-argument size limit as the registry grows. An annotation this reader
# does not know is an error, not a silent no-op.
params_file=".build/generate-params.json"
python_yaml - registry/qyl/policy.yaml > "${params_file}" <<'PY'
import glob
import json
import sys
import yaml

groups = {g["id"]: g for g in (yaml.safe_load(open(sys.argv[1])) or {}).get("groups") or []}

def qyl(node):
    return ((node or {}).get("annotations") or {}).get("qyl") or {}

def refs(group_id, allowed):
    out = []
    for attribute in groups[group_id].get("attributes") or []:
        annotations = qyl(attribute)
        unknown = set(annotations) - allowed
        if unknown:
            raise SystemExit(f"error: {group_id}: {attribute['ref']} has unknown qyl annotations {sorted(unknown)}")
        out.append((attribute["ref"], annotations))
    return out

persistence = refs("qyl.policy.persistence", {"persisted", "token_exempt", "sanitize"})
for key, annotations in persistence:
    if "sanitize" in annotations and annotations["sanitize"] != "exception_text":
        raise SystemExit(f"error: {key} declares an unsupported value sanitizer")
    if annotations.get("sanitize") and annotations.get("persisted") is False:
        raise SystemExit(f"error: {key} requires sanitization but is explicitly not persisted")
phase = refs("qyl.policy.phase", {"phase"})
spans = qyl(groups["qyl.policy.spans"]).get("spans") or {}
for span_id, entry in spans.items():
    unknown = set(entry) - {"starter", "domain", "fixed", "span_name"}
    if unknown:
        raise SystemExit(f"error: qyl.policy.spans: {span_id} has unknown keys {sorted(unknown)}")
    if entry.get("starter") and not entry.get("domain"):
        raise SystemExit(f"error: qyl.policy.spans: starter {span_id} names no domain")

# Vendor keys are exempt from the token rule only: a vendor spells its own tags.
vendor_keys = sorted({attribute["id"]
                      for path in glob.glob("registry/vendor/*.yaml")
                      for g in (yaml.safe_load(open(path)) or {}).get("groups") or []
                      for attribute in g.get("attributes") or [] if "id" in attribute})

group = qyl(groups["qyl.policy.persistence"])
policy = {
    "vendor_keys": vendor_keys,
    "not_persisted": sorted(key for key, a in persistence if a.get("persisted") is False),
    "not_persisted_prefixes": sorted(group.get("not_persisted_prefixes") or []),
    "denied_key_tokens": sorted(group.get("denied_key_tokens") or []),
    "token_exempt": sorted(key for key, a in persistence if a.get("token_exempt") is True),
    "exception_text": sorted(key for key, a in persistence if a.get("sanitize") == "exception_text"),
    "captured_fields": group["captured_fields"],
    "phase_end": sorted(key for key, a in phase if a.get("phase") == "end"),
    "spans": {span_id: {"starter": bool(entry.get("starter")),
                        "domain": entry.get("domain") or "",
                        "fixed": entry.get("fixed") or {},
                        "templates": (entry.get("span_name") or {}).get("templates") or [],
                        "substitutions": (entry.get("span_name") or {}).get("substitutions") or {}}
              for span_id, entry in sorted(spans.items())},
}
for key in ("not_persisted", "denied_key_tokens", "token_exempt", "phase_end", "vendor_keys"):
    if not policy[key]:
        raise SystemExit(f"error: registry/qyl/policy.yaml declares no {key}")
print(json.dumps({"params": {"policy": policy}}, separators=(",", ":"), sort_keys=True))
PY

"${WEAVER_CMD[@]}" registry check -r registry
"${WEAVER_CMD[@]}" registry check -r registry --v2 --include-unreferenced -p registry/policies-v2
python3 scripts/check-future.py

# Weaver writes files and never deletes one, so a root the registry or a filter stops producing
# (every host.* attribute is resource-only and gets no setter) would leave its stale file behind.
# The two Generated trees and the TypeScript package's src/ and dist/ are wholly generated; clear
# them first.
rm -rf src/Qyl.Telemetry.SemanticConventions/Generated src/Qyl.Telemetry.SemanticConventions.Incubating/Generated \
  typescript/src typescript/dist

# One `weaver registry generate` per target under templates/registry, all with the same registry,
# flags and parameters.
weaver_generate() {
  "${WEAVER_CMD[@]}" registry generate \
    -r registry \
    --v2 \
    --include-unreferenced \
    -t templates \
    -D "weaver_version=${actual_weaver_version}" \
    -D "core_ref=${core_ref}" \
    -D "core_commit=${core_commit}" \
    -D "genai_commit=${genai_commit}" \
    -D "scope_names=${scope_names}" \
    -D "vendor_activity_sources=${vendor_activity_sources}" \
    --params "${params_file}" \
    "$@" \
    .
}

# The resolved registry first, as .build/registry.json. Its SHA-256 without the local paths of
# fetched dependencies, so a checkout or cache location does not move it, is the fingerprint both
# language targets carry: C# RegistrySnapshot.Sha256 and TypeScript REGISTRY_SNAPSHOT_SHA256.
weaver_generate snapshot
registry_snapshot_sha256="$(python3 - .build/registry.json <<'PY'
import hashlib
import json
import sys

def semantic(value):
    if isinstance(value, dict):
        result = {}
        for key, item in value.items():
            if key == "provenance":
                item = {field: data for field, data in item.items()
                        if field != "path" or data.startswith(("https://", "http://"))}
            result[key] = semantic(item)
        return result
    if isinstance(value, list):
        return [semantic(item) for item in value]
    return value

with open(sys.argv[1], encoding="utf-8") as snapshot:
    canonical = json.dumps(semantic(json.load(snapshot)), sort_keys=True, separators=(",", ":"))
print(hashlib.sha256(canonical.encode()).hexdigest())
PY
)"

# The token rule keeps free text out of storage. A registry enum attribute carries one of its
# registry members instead (cicd.pipeline.result: success, failure, ...), so every enum key that
# contains a denied token joins the token exemptions here, once the snapshot has resolved each
# attribute's type and before the targets render params.policy. A top-level definition wins
# over a dependency's, as in the template filters.
python3 - .build/registry.json "${params_file}" <<'PY'
import json
import sys

registry_path, params_path = sys.argv[1:]
with open(registry_path, encoding="utf-8") as snapshot:
    registry = json.load(snapshot)
with open(params_path, encoding="utf-8") as params_json:
    params = json.load(params_json)
policy = params["params"]["policy"]

types = {}
for source in [registry, *(registry.get("dependencies") or {}).values()]:
    for attribute in (source.get("registry") or {}).get("attributes") or []:
        types.setdefault(attribute["key"], attribute.get("type"))
enum_keys = {key for key, kind in types.items()
             if isinstance(kind, dict) and "members" in kind
             and any(token in key.lower() for token in policy["denied_key_tokens"])}
policy["token_exempt"] = sorted(set(policy["token_exempt"]) | enum_keys)
with open(params_path, "w", encoding="utf-8") as params_json:
    params_json.write(json.dumps(params, separators=(",", ":"), sort_keys=True) + "\n")
PY

for target in csharp typescript; do
  weaver_generate -D "registry_snapshot_sha256=${registry_snapshot_sha256}" "${target}"
done
cp LICENSE typescript/LICENSE

python3 scripts/generate-live-check.py .build/registry.json "${params_file}" registry/.weaver.toml

"${script_dir}/generate-reference.sh"

echo "Regenerated from registry/ with Weaver ${actual_weaver_version}"
echo "  core:  ${core_ref} (${core_commit})"
echo "  genai: ${genai_commit}"
echo "  registry snapshot: ${registry_snapshot_sha256}"
