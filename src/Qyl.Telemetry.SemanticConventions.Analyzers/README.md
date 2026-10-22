# Qyl.Telemetry.SemanticConventions.Analyzers

Roslyn analyzers and code fixes for OpenTelemetry semantic-convention consumers. They flag, at
compile time, a deprecated attribute key with its replacement, a schema URL mistakenly stored
as a resource attribute, a required attribute that is missing on a GenAI or HTTP span, a span started
outside the generated starters, a composed span name or business key, a business key the
collector drops, a contrib instrumentation package next to `AddQyl`, a `catch` that rethrows
without `RecordFailure`, and a wider band of instrumentation patterns that produce telemetry a
collector cannot use. They read typed setters and starters, not only `SetTag`.

```bash
dotnet add package Qyl.Telemetry.SemanticConventions.Analyzers
```

The package is a development dependency; `dotnet add package` writes `PrivateAssets="all"` for
it, so nothing flows to consumers of your own package. Pair it with
[`Qyl.Telemetry.SemanticConventions`](https://www.nuget.org/packages/Qyl.Telemetry.SemanticConventions),
whose generated constants are what the fixes rewrite to.

## Tuning

Every rule has an id, a default severity and a code fix where one is possible. Severity is set
the usual way in `.editorconfig`; the repository ships ready-made
[severity profiles](https://github.com/ANcpLua/Qyl.Telemetry.SemanticConventions/tree/main/docs/editorconfig)
for strict, default and migration-only use.

## More

- Every rule with its rationale:
  [rule catalog](https://github.com/ANcpLua/Qyl.Telemetry.SemanticConventions/blob/main/docs/Qyl.Telemetry.SemanticConventions.Analyzers.md)
- Which deprecated key maps to which live one:
  [migration catalog](https://github.com/ANcpLua/Qyl.Telemetry.SemanticConventions/blob/main/docs/migration-catalog.md)
- Changes per version:
  [GitHub releases](https://github.com/ANcpLua/Qyl.Telemetry.SemanticConventions/releases)
