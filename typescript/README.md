# @ancplua/qyl-semantic-conventions

ES modules and TypeScript literal declarations generated from the same resolved Weaver registry as qyl's C# packages. No runtime dependencies. Individual named exports are tree-shakable.

```ts
import { ATTR_SERVICE_NAME, SCHEMA_URL } from '@ancplua/qyl-semantic-conventions';
import {
  ATTR_MCP_METHOD_NAME,
  METRIC_MCP_SERVER_OPERATION_DURATION,
  METRIC_MCP_SERVER_OPERATION_DURATION_UNIT,
} from '@ancplua/qyl-semantic-conventions/incubating';
```

The root export contains stable and deprecated migration conventions. `./incubating` contains the complete vocabulary, including stable exports and qyl-owned development conventions. Upstream unstable conventions can change in minor releases. Deprecated keys remain migration symbols; importing them does not normalize telemetry.

Exports follow OpenTelemetry JavaScript names: `ATTR_*`, enum `*_VALUE_*`, `METRIC_*` and `EVENT_*`. Metrics also export `METRIC_*_UNIT` and `METRIC_*_DEFINITION` (name, instrument, unit and stability). `SCHEMA_URL` identifies the qyl schema; `REGISTRY_SNAPSHOT_SHA256` identifies the shared resolved input, excluding source-location metadata such as temporary checkout paths. The C# `RegistrySnapshot.Sha256` constant carries the same fingerprint. Literal `.d.ts` declarations and TypeScript sources are included alongside JavaScript.

Import `findAttribute` and `findMetric` from `@ancplua/qyl-semantic-conventions/catalog`
for value kinds, template expansion, units, stability and deprecation metadata. Lookups
return frozen records from the same normalized Weaver facts as C# `RegistryCatalog`.
An unknown custom name returns undefined and remains valid OpenTelemetry. Requirements
belong to the selected signal definition; registry membership does not establish scope ownership.
The separate export keeps the complete catalog out of constant-only imports.

Import `getCapturedFieldPersistence` from `@ancplua/qyl-semantic-conventions/policy`
to classify captured HTTP header and RPC metadata names using the same registry policy as
C# `RegistryPolicy.TryGetCapturedFieldPersistence`. It returns `true` for valid names,
`false` for bare roots or invalid names in those families, and `undefined` for unrelated keys.
Matching is case-insensitive for ASCII. Callers still own capture selection and value-shape checks.

Generation runs through `scripts/generate.sh` at the repository root, using pinned official language-independent OpenTelemetry registries and qyl/vendor definitions. These bindings can release independently of OpenTelemetry SDK versions. SDKs/exporters and official OTLP wire definitions remain separate dependencies. The registry schema URL alone does not transform attributes.
