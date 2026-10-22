#!/usr/bin/env bash
# Pack the three NuGet packages and the TypeScript package, publish both with the keys registered
# in Vitals, tag v<x.y.z> and create the GitHub release. No workflow publishes.
#
#   ./release.sh <x.y.z>            pack, publish, tag, GitHub release
#   ./release.sh <x.y.z> --dry-run  pack, and run npm's publish --dry-run; nothing is pushed or tagged
#
# Directory.Build.props VersionPrefix, typescript/package.json, registry/manifest.yaml schema_url
# (https://qyl.at/schemas/<x.y.z>) must already agree on HEAD,
# with scripts/generate.sh run and committed.
set -euo pipefail
cd "$(dirname "$0")"

version=${1:-}
dry=0
[[ ${2:-} == --dry-run ]] && dry=1
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "usage: $0 <x.y.z> [--dry-run]" >&2; exit 2; }
die() { echo "release: $*" >&2; exit 1; }

NUGET_KEY_FILE=$HOME/.config/vitals/nuget-qyl-manual-publish-key  # vitals keys: "qyl-dev manual publish and release (NuGet)"
NPM_TOKEN_FILE=$HOME/.config/vitals/npm-qyl-manual-publish-token
NPM_REGISTRY=https://registry.npmjs.org
OUT=$PWD/artifacts
TGZ=$OUT/ancplua-qyl-semantic-conventions-$version.tgz
PACKAGES=(Qyl.Telemetry.SemanticConventions Qyl.Telemetry.SemanticConventions.Analyzers Qyl.Telemetry.SemanticConventions.Incubating)

[[ -r $NUGET_KEY_FILE ]] || die "NuGet key missing: $NUGET_KEY_FILE (vitals keys)"
[[ -r $NPM_TOKEN_FILE ]] || die "npm token missing: $NPM_TOKEN_FILE (vitals keys)"
[[ $(git branch --show-current) == main ]] || die "not on main"
[[ -z $(git status --porcelain --untracked-files=no) ]] || die "working tree not clean"
git fetch -q origin main
[[ $(git rev-parse HEAD) == $(git rev-parse origin/main) ]] || die "main differs from origin/main"
git rev-parse -q --verify "refs/tags/v$version" >/dev/null && die "tag v$version already exists"

prefix=$(sed -n 's:.*<VersionPrefix[^>]*>\([^<]*\)</VersionPrefix>.*:\1:p' Directory.Build.props | head -1)
schema=$(sed -n 's#^schema_url: https://qyl.at/schemas/##p' registry/manifest.yaml)
npm_version=$(node -p "require('./typescript/package.json').version")
[[ $prefix == "$version" ]] || die "Directory.Build.props VersionPrefix is $prefix, not $version"
[[ $npm_version == "$version" ]] || die "typescript/package.json version is $npm_version, not $version"
[[ $schema == "$version" ]] || die "registry/manifest.yaml schema_url names $schema, not $version"

# The commit subjects since the previous v* tag, grouped by conventional prefix; release commits are left out.
release_notes() {
  local previous range subjects group heading lines
  previous=$(git describe --tags --abbrev=0 --match 'v*' "$1^" 2>/dev/null || true)
  range=${previous:+$previous..}$1
  subjects=$(git log --no-merges --format='%s' "$range" | grep -v '^release: ' || true)
  [[ -n $previous ]] && printf 'Changes since %s.\n' "$previous"
  for group in 'feat:Features' 'fix:Fixes' 'perf:Performance' 'refactor:Refactoring' 'build:Build' 'test:Tests' 'docs:Documentation' 'chore:Maintenance' 'generate:Generation'; do
    heading=${group#*:}
    lines=$(grep -E "^${group%%:*}(\([^)]*\))?!?: " <<<"$subjects" | sed -E 's/^[a-z]+(\([^)]*\))?!?: /- /' || true)
    [[ -n $lines ]] && printf '\n## %s\n\n%s\n' "$heading" "$lines"
  done
  lines=$(grep -vE '^(feat|fix|perf|refactor|build|test|docs|chore|generate)(\([^)]*\))?!?: ' <<<"$subjects" | sed 's/^/- /' || true)
  [[ -n $lines ]] && printf '\n## Other\n\n%s\n' "$lines"
  return 0
}

slnx=$(find . -maxdepth 1 -type f \( -name '*.slnx' -o -name '*.sln' \) -print -quit | sed 's#^\./##')
[[ -n $slnx ]] || die "no .slnx or .sln at the repo root"
mkdir -p "$OUT"
find "$OUT" -maxdepth 1 \( -name '*.nupkg' -o -name '*.tgz' \) -delete
dotnet restore "$slnx"
dotnet build "$slnx" -c Release --no-restore -p:Version="$version"
dotnet pack "$slnx" -c Release --no-build -o "$OUT" -p:Version="$version"
npm pack ./typescript --pack-destination "$OUT" >/dev/null
for id in "${PACKAGES[@]}"; do [[ -f $OUT/$id.$version.nupkg ]] || die "pack did not produce $id.$version.nupkg"; done
[[ -f $TGZ ]] || die "pack did not produce $TGZ"

if (( dry )); then
  NPM_CONFIG_TOKEN=$(<"$NPM_TOKEN_FILE") python3 eng/release/publish-npm.py bun publish --cwd typescript "$TGZ" --registry "$NPM_REGISTRY" --access public --dry-run </dev/null
  echo "dry run finished: nothing published, nothing tagged"
  exit 0
fi

for id in "${PACKAGES[@]}"; do
  NUGET_API_KEY="$(<"$NUGET_KEY_FILE")" dotnet nuget push "$OUT/$id.$version.nupkg" --source https://api.nuget.org/v3/index.json --skip-duplicate
done
NPM_CONFIG_TOKEN=$(<"$NPM_TOKEN_FILE") python3 eng/release/publish-npm.py bun publish --cwd typescript "$TGZ" --registry "$NPM_REGISTRY" --access public </dev/null ||
  die "npm publication failed; if a token or OTP is requested, renew the credential and rerun"

notes=$(mktemp); trap 'rm -f "$notes"' EXIT
release_notes HEAD >"$notes"
git tag -a "v$version" -m "v$version"
git push -q origin "v$version"
gh release create "v$version" --title "v$version" --notes-file "$notes" "$OUT"/Qyl.Telemetry.SemanticConventions*."$version".nupkg "$TGZ"
