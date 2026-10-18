#!/usr/bin/env bash
# Weaver owns Markdown rendering. JSON/YAML evidence is template data, not a renderer.
set -euo pipefail
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$root"
output=${1:-docs/reference}
read -r -a weaver_command <<< "${WEAVER:-weaver}"
expected=$(awk -F '[<>]' '/<WeaverVersion>/{print $3}' Version.props)
actual=$("${weaver_command[@]}" --version | awk '{print $2}')
if [[ "$actual" != "$expected" ]]; then
  echo "Reference generation requires the pinned Weaver $expected; found $actual." >&2
  exit 1
fi
"${weaver_command[@]}" registry generate \
  -r registry --include-unreferenced -t templates \
  --params docs/evidence/openai-reference.json \
  -D "weaver_version=$actual" \
  -D "chrome=$(cat docs/chatgpt-chrome-telemetry.json)" \
  markdown "$output"
echo "Generated references with Weaver $actual in $output"
