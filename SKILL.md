# Qyl.Telemetry.SemanticConventions: release

Publishes four artifacts at one version: NuGet `Qyl.Telemetry.SemanticConventions`, `.Analyzers` and `.Incubating`, and npm `@ancplua/qyl-semantic-conventions` from `typescript/`.

Version sources that agree on `main`: `Directory.Build.props` `VersionPrefix`, `registry/manifest.yaml` `schema_url` (`https://qyl.at/schemas/<x.y.z>`), `typescript/package.json` `version`, and a `## [x.y.z]` entry on top of `CHANGELOG.md`. Run `scripts/generate.sh` after the bump and commit its output. A published version is immutable: pick one above the latest on nuget.org.

`./release.sh <x.y.z>` packs, publishes to NuGet and npm, tags `v<x.y.z>` and creates the GitHub release. Keys: npm `~/.config/vitals/npm-qyl-manual-publish-token`, NuGet `~/.config/vitals/nuget-qyl-manual-publish-key`; never print or commit a key.

Consumers re-pin the version themselves: AutoInstrumentation `Directory.Packages.props`, qyl `Version.props` `QylSemanticConventionsVersion`, qyl.mcp `package.json`.
