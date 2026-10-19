#!/usr/bin/env bash
# The gate's drift check: regenerate everything from registry/ and fail if the result differs from
# what is committed. Same shape as the `check-generated` target in
# opentelemetry-weaver-examples/basic/Makefile.
#
# The comparison is against HEAD, not against the index. `git diff` alone compares the working
# tree to the index, and scripts/generate.sh has already overwritten the working tree by the
# time it runs — so a generated file edited but not committed, or staged but not committed,
# reported OK while the committed tree was stale.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
cd "${repo_root}"

"${script_dir}/generate.sh"

generated_paths=(
  "registry/.weaver.toml"
  "src/Qyl.Telemetry.SemanticConventions/Generated"
  "src/Qyl.Telemetry.SemanticConventions.Incubating/Generated"
  "src/Qyl.Telemetry.SemanticConventions.Analyzers/SemconvRegistryFacts.g.cs"
  "src/Qyl.Telemetry.SemanticConventions.Analyzers/SemconvDeprecations.g.cs"
  "src/Qyl.Telemetry.SemanticConventions.Analyzers/SemconvTypedSurface.g.cs"
  "generated"
  "typescript/src"
  "typescript/dist"
  "typescript/package.json"
  "typescript/LICENSE"
  "docs/reference"
)

if ! git diff HEAD --exit-code -- "${generated_paths[@]}"; then
  echo "FAIL: generated code differs from HEAD." >&2
  echo "Run scripts/generate.sh and commit the result." >&2
  exit 1
fi

untracked="$(git ls-files --others --exclude-standard -- "${generated_paths[@]}")"
if [[ -n "${untracked}" ]]; then
  echo "FAIL: generation produced files that are not committed:" >&2
  echo "${untracked}" >&2
  exit 1
fi

echo "OK: generated code is up to date"
