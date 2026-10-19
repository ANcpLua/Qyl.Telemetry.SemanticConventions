# Qyl.Telemetry.SemanticConventions

Weaver generates C# and TypeScript bindings from OpenTelemetry, qyl and vendor registries. registry/manifest.yaml and registry/vendor own coverage; Version.props pins Weaver. scripts/generate.sh updates outputs. Convention, qyl package, vendor library and SDK versions are independent. SKILL.md covers releases and consumers.

Commit and push regenerated outputs before the gate; scripts/check-generated.sh compares HEAD.
