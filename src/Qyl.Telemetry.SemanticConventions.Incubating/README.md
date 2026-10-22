# Qyl.Telemetry.SemanticConventions.Incubating

The incubating OpenTelemetry semantic conventions as .NET constants and typed helpers, generated
by [Weaver](https://github.com/open-telemetry/weaver) from qyl's registry and shipped pre-built:
attribute keys at every stability tier, the metric, span, event and entity rows the stable tier
does not carry and their value-set structs, the incubating span starters `Qyl.IncubatingSpans`
(messaging process consumer, invoke_agent, execute_tool and its skill specializations) with
their views, the qyl-owned instrument names, and `AttributeMapping`, the collector's normalize
table.

Incubating keys track unstable upstream conventions and may change between minor releases. Opt
in deliberately, and pin the version.

```bash
dotnet add package Qyl.Telemetry.SemanticConventions.Incubating
```

```csharp
using System.Diagnostics;
using Qyl.Telemetry.SemanticConventions.Incubating.Activities;
using Qyl.Telemetry.SemanticConventions.Incubating.Mapping;

using var activity = new Activity("GET").Start();
activity.SetHttpRequestBodySize(1234);

// The collector's normalize table: deprecated key -> live key, obsoletion, namespace.
Console.WriteLine(AttributeMapping.TryGetRename("http.method", out var live) ? live : "(none)");
Console.WriteLine(AttributeMapping.IsObsoleted("db.connection_string"));   // True
Console.WriteLine(AttributeMapping.NamespaceOf("qyl.session.id"));         // qyl
```

This package and
[`Qyl.Telemetry.SemanticConventions`](https://www.nuget.org/packages/Qyl.Telemetry.SemanticConventions)
are disjoint for every kind but attributes, so both namespaces can be in scope at once without an
ambiguous call. `AttributeMapping` is switch statements over string literals; nothing reflects,
and a consumer publishes NativeAOT clean.

## More

- The registry, its policies and how generation works:
  [repository README](https://github.com/ANcpLua/Qyl.Telemetry.SemanticConventions#readme)
- Changes per version:
  [GitHub releases](https://github.com/ANcpLua/Qyl.Telemetry.SemanticConventions/releases)
