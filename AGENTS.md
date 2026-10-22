# Qyl.Telemetry.SemanticConventions

Weaver generates C# and TypeScript bindings from OpenTelemetry, qyl and vendor registries. registry/manifest.yaml and registry/vendor own coverage; Version.props pins Weaver. scripts/generate.sh updates outputs; commit and push them before `mise run check`. Release: ./release.sh <x.y.z>.

Vendor registries keep each producer's native spelling and evidence boundary: a key with several wire representations stays `any`, a colliding C# constant takes a `Native` suffix (TypeScript `ATTR_*_NATIVE`), and the inactive ChatGPT for Chrome hooks (`browser_use.*`, `codex.browser_use.security_check`) stay out of the registry.
