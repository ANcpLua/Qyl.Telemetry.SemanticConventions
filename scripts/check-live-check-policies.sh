#!/usr/bin/env bash
# The gate on the live-check advice policy set. registry/policies/live_check_advice/ and
# registry/.weaver.toml decide what level a finding on ingested telemetry is reported at, and
# both Qyl.Telemetry.AutoInstrumentation and the collector run live-check with them; this
# script pins the exact findings the set produces, id and level, on samples that cover every
# rule.
#
# Three runs, because the set has two halves and both are load-bearing:
#
#   1. The full sample with --config and --advice-policies. Every attribute must produce
#      exactly the findings the table below names -- those ids, those levels, no more and no
#      fewer. The sample deliberately contains the three cases that stay violations, so the
#      run exits 1.
#   2. The reproduction from the first AutoInstrumentation live-check run, with the same two
#      flags. A renamed key, an open-enum value and a coercible int: none of it is qyl's
#      defect and none of it may fail the build, so this run exits 0 under
#      `--fail-on violation`.
#   3. The full sample with --advice-policies but no --config. Weaver's built-in advisors are
#      compiled into the binary and `--advice-policies` replaces only the rego ones, so
#      without the config the built-in `deprecated`, `type_mismatch` and
#      `undefined_enum_variant` findings are raised alongside the qyl ones and the run fails.
#      This asserts that failure, so the day Weaver makes the built-ins overridable the
#      assertion breaks and the config can go.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
cd "${repo_root}"

WEAVER="${WEAVER:-weaver}"
read -r -a WEAVER_CMD <<< "${WEAVER}"

policies="registry/policies/live_check_advice"
config="registry/.weaver.toml"
cases="${policies}/samples/advice-cases.json"
repro="${policies}/samples/autoinstrumentation-repro.json"

full_report="$(mktemp)"
repro_report="$(mktemp)"
unconfigured_report="$(mktemp)"
trap 'rm -f "${full_report}" "${repro_report}" "${unconfigured_report}"' EXIT

live_check() {
  local output="$1"
  shift
  set +e
  "${WEAVER_CMD[@]}" registry live-check \
    -r registry \
    --include-unreferenced \
    --advice-policies "${policies}" \
    --no-stream \
    --fail-on violation \
    --format json \
    "$@" \
    > "${output}" 2>/dev/null
  local status=$?
  set -e
  echo "${status}"
}

full_exit="$(live_check "${full_report}" --config "${config}" --input-source "${cases}")"
repro_exit="$(live_check "${repro_report}" --config "${config}" --input-source "${repro}")"
unconfigured_exit="$(live_check "${unconfigured_report}" --input-source "${cases}")"

python3 - \
  "${full_report}" "${full_exit}" \
  "${repro_report}" "${repro_exit}" \
  "${unconfigured_report}" "${unconfigured_exit}" <<'PY'
import json
import sys

(
    full_path,
    full_exit,
    repro_path,
    repro_exit,
    unconfigured_path,
    unconfigured_exit,
) = sys.argv[1:7]

# span name -> [(attribute key, sorted "id:level" list)], in the order the sample declares
# them. An empty list means the attribute must draw no advice at all.
EXPECTED = {
    # Registered native spellings coexist with namespaces; undeclared children still fail.
    "qyl.live_check.vendor_spellings": [
        ("error", ["not_stable:improvement"]),
        ("sessionId", ["not_stable:improvement"]),
        ("_scrubError", ["not_stable:improvement"]),
        ("prompt.id", ["not_stable:improvement"]),
        ("error.unknown_child", [
            "extends_namespace:information",
            "illegal_namespace:violation",
            "missing_attribute:violation",
        ]),
    ],
    "qyl.live_check.open_enum": [
        # error.type carries the member `_OTHER`, so both values are prescribed, not defects.
        ("error.type", ["open_enum_value:information"]),
        ("error.type", ["open_enum_value:information"]),
    ],
    "qyl.live_check.closed_enum": [
        ("network.transport", ["undocumented_enum_value:information"]),
    ],
    "qyl.live_check.documented_enum": [
        ("network.transport", []),
    ],
    "qyl.live_check.deprecated_renamed": [
        ("rpc.system", ["deprecated_renamed:improvement", "not_stable:improvement"]),
        ("az.namespace", ["deprecated_renamed:improvement", "not_stable:improvement"]),
        ("db.system", ["deprecated_renamed:improvement", "not_stable:improvement"]),
        ("db.operation", ["deprecated_renamed:improvement", "not_stable:improvement"]),
        ("messaging.operation", ["deprecated_renamed:improvement", "not_stable:improvement"]),
    ],
    "qyl.live_check.deprecated_obsoleted": [
        ("exception.escaped", ["deprecated_obsoleted:improvement"]),
    ],
    "qyl.live_check.type_coercible": [
        ("messaging.message.body.size", ["not_stable:improvement", "type_coercible:improvement"]),
        ("http.response.status_code", ["type_coercible:improvement"]),
    ],
    "qyl.live_check.type_not_coercible": [
        ("messaging.message.body.size", ["not_stable:improvement", "type_not_coercible:violation"]),
    ],
    # The seven keys 9.1.0 added to registry/vendor/. A declared key draws no
    # `missing_attribute`; `not_stable` is Weaver's default for a development row and stays.
    "qyl.live_check.vendor_keys": [
        ("soap.message_version", ["not_stable:improvement"]),
        ("soap.reply_action", ["not_stable:improvement"]),
        ("wcf.channel.path", ["not_stable:improvement"]),
        ("wcf.channel.scheme", ["not_stable:improvement"]),
        ("az.schema_url", ["not_stable:improvement"]),
        ("az.client_request_id", ["not_stable:improvement"]),
        ("db.elasticsearch.schema_url", ["not_stable:improvement"]),
    ],
    # Undeclared keys retain the upstream name and namespace checks.
    "qyl.live_check.name_rules": [
        ("NotANamespacedKey", [
            "invalid_format:violation",
            "missing_attribute:violation",
            "missing_namespace:improvement",
        ]),
    ],
    # A key extends a template only across a dot (Weaver 0.27.0). `headers.host` shares the
    # template's prefix but not its namespace, so it is an undeclared key, not an instance.
    "qyl.live_check.template_boundary": [
        ("http.request.header.host", ["template_attribute:information"]),
        ("http.request.headers.host", [
            "extends_namespace:information",
            "missing_attribute:violation",
        ]),
    ],
}

EXPECTED_REPRO = {
    "qyl.live_check.autoinstrumentation_repro": [
        ("az.namespace", ["deprecated_renamed:improvement", "not_stable:improvement"]),
        ("error.type", ["open_enum_value:information"]),
        ("messaging.message.body.size", ["not_stable:improvement", "type_coercible:improvement"]),
    ],
}

# Findings the built-in advisors raise and registry/.weaver.toml drops so the qyl rules can
# replace them. None of these may appear in a configured run.
REPLACED = {"deprecated", "type_mismatch", "undefined_enum_variant"}


def observed(path):
    with open(path, encoding="utf-8") as handle:
        report = json.load(handle)
    result = {}
    for sample in report["samples"]:
        span = sample["span"]
        result[span["name"]] = [
            (
                attribute["name"],
                sorted(
                    f"{finding['id']}:{finding['level']}"
                    for finding in attribute["live_check_result"]["all_advice"]
                ),
            )
            for attribute in span["attributes"]
        ]
    return result


def compare(label, expected, got, problems):
    if set(got) != set(expected):
        problems.append(f"{label}: expected spans {sorted(expected)}, got {sorted(got)}")
        return
    for span_name, expected_rows in expected.items():
        got_rows = got[span_name]
        if len(got_rows) != len(expected_rows):
            problems.append(
                f"{label}/{span_name}: expected {len(expected_rows)} attributes, "
                f"got {len(got_rows)}"
            )
            continue
        for index, (want, have) in enumerate(zip(expected_rows, got_rows)):
            if want != have:
                problems.append(
                    f"{label}/{span_name}[{index}]: expected {want[0]} -> {want[1]}, "
                    f"got {have[0]} -> {have[1]}"
                )


problems = []

full = observed(full_path)
compare("cases", EXPECTED, full, problems)

repro = observed(repro_path)
compare("repro", EXPECTED_REPRO, repro, problems)

for label, report in (("cases", full), ("repro", repro)):
    for span_name, rows in report.items():
        for key, advice in rows:
            for finding in advice:
                if finding.split(":", 1)[0] in REPLACED:
                    problems.append(
                        f"{label}/{span_name}/{key}: built-in finding '{finding}' was not "
                        "dropped; registry/.weaver.toml no longer filters it"
                    )

if full_exit != "1":
    problems.append(
        f"the run over {full_path} exited {full_exit}; the sample carries "
        "type_not_coercible, the two name violations on NotANamespacedKey and a key past a template "
        "boundary, so --fail-on violation must exit 1"
    )

if repro_exit != "0":
    problems.append(
        f"the AutoInstrumentation reproduction exited {repro_exit}; a renamed key, an "
        "open-enum value and a coercible int are none of them qyl's defect, so "
        "--fail-on violation must exit 0"
    )

if unconfigured_exit == "0":
    problems.append(
        "the run without --config exited 0; Weaver's built-in deprecated and type_mismatch "
        "advisors are supposed to raise violations there, which is the whole reason "
        "registry/.weaver.toml exists"
    )

built_ins = {
    finding.split(":", 1)[0]
    for rows in observed(unconfigured_path).values()
    for _, advice in rows
    for finding in advice
} & REPLACED
if built_ins != REPLACED:
    problems.append(
        f"the run without --config raised {sorted(built_ins)}; the sample is supposed to "
        f"trigger all of {sorted(REPLACED)} from the built-in advisors"
    )

if problems:
    print("FAIL: live-check advice policies do not produce the pinned findings.", file=sys.stderr)
    for problem in problems:
        print(f"  {problem}", file=sys.stderr)
    raise SystemExit(1)

print(
    f"OK: live-check advice policies produce the pinned findings on "
    f"{len(EXPECTED) + len(EXPECTED_REPRO)} spans"
)
PY
