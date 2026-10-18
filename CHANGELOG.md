# Changelog

## [Unreleased]

- Add native Anthropic telemetry coverage for Claude Code 2.1.302, Cowork and Claude Desktop 2.42304.0, the Claude API and TypeScript SDK 0.135.0, and Claude for Chrome 1.0.101. Include beta spans, log and span events, metrics, native attributes and documented dynamic naming paths.
- Preserve native keys and mixed wire representations, referencing shared upstream definitions.
- Preserve generated enum output while replacing repeated member scans with sorted grouping.
- Resolve C# identifier collisions, retain TypeScript native-key aliases, and accept registered vendor spellings in live checks while retaining undeclared-key checks.
- Audit ChatGPT Chrome extension 1.26.1005.11759 and document its inactive tracing hooks and separate Statsig event vocabulary.
- Add a reproducible Markdown export for the local and vendor registries at every stability.

## [14.1.0] - 2026-10-18

- Export `getCapturedFieldPersistence` from the TypeScript package's `policy` entry point, generated from the same registry-owned captured-field roots and character sets as C#.
- Preserve valid, invalid and unrelated field classifications across both languages, including case handling and protocol-specific field-name validation.

## [14.0.0] - 2026-10-17

- Preserve every valid explicitly captured HTTP-header and RPC-metadata name, including credential-like names and binary metadata names. Capture selection controls exposure; unrelated persistence rules remain unchanged.
- Remove the captured-field name and token deny tables from the generated public policy. Keep protocol name validation and rejection of bare template roots.

## [13.2.0] - 2026-10-17

- Replace blanket HTTP-header and RPC-metadata retention denials with a finite field-name and credential-name policy. Preserve explicit denials and exception sanitization; allow benign selected names such as `x-message-id`.
- Generate captured-field classification from the canonical registry policy. The Collector consumes the generated captured-field classification; policy tests verify malformed prefixes and known credential names.

## [13.1.2] - 2026-10-17

- Generate only the key-kind and metric-name facts used by the analyzer instead of embedding unused public catalog metadata.
- Share the value-kind vocabulary, replace handwritten legacy compiler polyfills with the shared source package, and align source folders with their namespaces.
- Correct the failure-recording documentation to describe sanitized exception persistence.

## [13.1.1] - 2026-10-17

- Keep the exception-text policy limited to the two canonical keys. Deny case variants and dotted descendants, preserving the previous child-key privacy boundary when the canonical fields become eligible for sanitized persistence.

## [13.1.0] - 2026-10-17

- Retain `exception.message` and `exception.stacktrace` only after Collector value sanitization. Remove their exact-key denials and exempt only `exception.message` from the unchanged general message-token rule.
- Generate the required exception-text policy alongside the existing denial tables. Key exemptions do not bypass value sanitization or earlier privacy denials.
- Define a bounded, value-free diagnostic counter for exception-attribute sanitization failures.

## [13.0.0] - 2026-10-16

- Remove `SetAppTag`, the `qyl.app` string template and the stringifying `App` span views. Applications use native typed attributes with their own names; historical keys have no invented registry type.
- Allow application-owned `ActivitySource` instances. Keep generated starters optional and limit the constant-name rule to `Spans.Work`. Remove the helper-specific QYL0113 rule.
- Define `qyl.collector.attributes.coalesced` for lossless normalization of identical typed duplicates, and describe attribute drops as explicit policy denials rather than unknown registry names.

## [12.0.0] - 2026-10-16

- Correct the Oracle vendor declaration for `db.odp.roundtrip.duration` from double to string, matching real OTLP output from the pinned provider. The generated C# setter now accepts a string; the C# and TypeScript catalogs retain the observed representation without guessing a unit.
- Verify native tests against OpenTelemetry SDK 1.20.0. This test dependency is not shipped in the convention packages.

Notable changes to the `Qyl.Telemetry.SemanticConventions` package family. `VersionPrefix` in
[`Directory.Build.props`](Directory.Build.props) names the current release line and must equal the registry
`schema_url` version, which qyl.at serves before a release. A release is packed and pushed from the developer's machine
(the three NuGet packages and the TypeScript npm package), then tagged `v<version>`;
[`eng/release/verify-packages.sh`](eng/release/verify-packages.sh) proves the published packages against nuget.org.

## [11.0.0] - 2026-10-16

### Added

- `RegistryCatalog.TryGetAttribute` and `TryGetMetric` expose generated value kinds, templates, instrument units, stability and deprecations. The TypeScript `./catalog` export reads the same normalized Weaver facts. An unknown custom name is not an OpenTelemetry error; required attributes remain tied to their signal definitions.

### Changed

- Replace the positional span snapshot constructor with identity plus named initialization. Resource and instrumentation scope carry independent schema URLs, typed attributes and dropped counts. Span, event and link snapshots retain OTLP flags, trace state and dropped counts; exact nanosecond timestamps remain available alongside .NET timestamp projections. The span kind retains the OTLP unspecified value instead of treating it as an internal operation.
- QYL0200 reserves qyl-owned names while allowing custom application and library telemetry. A name missing from the standard registry is not, by itself, invalid OpenTelemetry.
- QYL0006 now reports `telemetry.schema_url` resource attributes instead of guessing whether a resource configuration names a schema. The OpenTelemetry SDK takes a real schema URL through its `schemaUrl` parameter; unknown schemas remain unset.
- Remove QYL0201 and obsolete metric-attribute detection. QYL0402 checks real `Meter.CreateHistogram` calls; QYL0202 uses real SDK measurement APIs.
- Migrate the test suite to MSTest 4.5.1 and Microsoft.Testing.Platform 2.5.1, with framework-independent Roslyn test utilities 3.0.0. Real SDK tests cover metrics, resource schema metadata, and event/link attribute collections.

### Fixed

- Analyzer test runners now fail on Roslyn engine failures before selecting rule diagnostics, so a crashing analyzer cannot pass a negative test.
- Traverse constructor-backed `ActivityTagsCollection` payloads on real span events and links.
- Compare migration replacement arrays structurally when catalog entries become collection keys.
- Preserve newlines in generated deprecation notes instead of replacing them with spaces.
- Remove obsolete analyzer pages before writing their replacements, including case-only filename changes on macOS. The native coverage build resolves its solution explicitly and uses the supported Fallout process-argument API.

## [10.1.0] - 2026-10-15

### Fixed

- The analyzer documentation generator formats diagnostic-ID suffixes with `AsSpan(3)`, avoiding JetBrains' false `System.Range` resolution error with the aliased analyzer assembly.
- Clear the generator's redundant array declarations and reduce its solution-file constant to private visibility. JetBrains inspection reports no errors, warnings or suggestions in the generator.
- Clean redundant imports and collection declarations in the analyzers and test helpers. The analyzer package test opens its ZIP asynchronously with cancellation and reads its entry names once.

### Changed

- Align the stable, incubating, analyzer and TypeScript packages with the 10.1.0 registry schema URL.

## [10.0.1] - 2026-10-15

### Fixed

- `RegistryPolicy.IsPersisted` and QYL0113 hold a `qyl.app.` key to the registry's denied keys and prefixes by the part after the prefix: `SetAppTag("user.email", ...)` is reported and not persisted, like `user.email`.
- `gen_ai.request.max_tokens` and the `gen_ai.usage.{text,audio,image}.*_tokens` counts are token-exempt (numbers, not credentials).
- `build.error.message`, `nservicebus.originating_machine`, `nservicebus.processing_machine` and `db.odp.user.statement` are `persisted: false`: error text, host names and statements the registry denies elsewhere.

## [10.0.0]

The agent-first API of the qyl API DSL ADR (2026-10-14): a breaking release.

- Registry policy (`registry/qyl/policy.yaml`, handed to the templates as `-D policy=<json>`): `qyl.persisted: false` on the 42 keys the collector's `deniedExactKeys` listed (all but `gen_ai.agent.name`, which is now persisted), the denied-key tokens, the token-exempt keys, `qyl.phase: end` on the attributes known only when an operation ends, and the `qyl.span_name` chains and starters keyed by span refinement id.
- New `RegistryPolicy` table in the stable package (`NotPersistedKeys`, `NotPersistedPrefixes`, `DeniedKeyTokens`, `TokenExemptKeys`, `VendorKeys`, `IsPersisted`, `ContainsDeniedToken`); the qyl collector's catalog reads it instead of its own deny lists.
- Span definitions are keyed by refinement id: the gen_ai skill spans (`gen_ai.execute_tool.load_skill.internal`, `.read_skill_resource.internal`, `.command.internal`), `db.{mariadb,mysql,oracledb,postgresql,sql_server}.client`, the `messaging.<system>.*`, `dynamodb.*` and `aws.s3.client` rows are generated; the GenAI provider refinements stay out. `SpanDefinition` gains `Type`, `IsSpecialization` and `SpanName` (the name chain); its constructor changes.
- `AttributeRef` gains `Condition`, `Phase` (new `AttributePhase`) and `IsPersisted`; its constructor changes.
- Span starters (namespace `Qyl`): `Spans.DbClient` in the stable package and `IncubatingSpans.MessagingProcessConsumer`, `GenAiInvokeAgent`, `GenAiExecuteTool` and the skill specializations `GenAiExecuteToolLoadSkill`, `GenAiExecuteToolReadSkillResource`, `GenAiExecuteToolCommand` in Incubating. Required attributes are positional, conditionally required ones nullable without a default (omitting one is CS7036), recommended ones default to `null`; OptIn, end-phase and group-fixed attributes take no parameter. Each starter stamps its `qyl.instrumentation.domain`, takes its kind from the group, its name from the `qyl.span_name` chain, puts every supplied value in the creation tags, and returns `null` when nothing listens; the consumer starter takes the producer's `ActivityContext`.
- `Spans.Source`: the public `Qyl.Telemetry.Application` ActivitySource, version `SchemaUrl.QylVersion`, schema URL `SchemaUrl.Qyl`. `Spans.Work(name)` starts an internal span stamped domain `app`. `Spans.SetAppTag(key, value)` (string, long, double, bool) writes `qyl.app.<key>`.
- `Spans.RecordFailure(exception)` and `RecordFailure(ErrorType, description)`: status Error with a description bounded to `Spans.MaxStatusDescriptionLength` (256), `error.type`, and for an exception the exception event.
- `Qyl.SpanNames` (stable package): one method per `qyl.span_name` chain, for db, http, messaging, rpc, gen_ai and mcp spans, so interceptors and starters name spans the same way (`SpanNames.HttpServer("_OTHER", "/orders")` is `HTTP /orders`).
- `SpanRecord` (with `SpanEventRecord`, `SpanLinkRecord`) and `StoreIdentity` beside `SpanDefinition`: the contract `Qyl.Telemetry.Testing` builds on. Generated read-only views for the starter span types (`DbClientSpan` in `Qyl.Telemetry.SemanticConventions.Views`; `MessagingProcessConsumerSpan`, `GenAiInvokeAgentSpan`, `GenAiExecuteToolSpan` in `….Incubating.Views`) expose only persisted attributes (`Keys`), `Service`, `App` (`qyl.app.*` without the prefix) and, for a database span, `Store`; `spans.DbClient()` and its siblings select them by kind, required attributes and fixed values.
- Analyzers: new rules at Error — QYL0111 (no hand-made `ActivitySource` or raw `StartActivity` in application code; `OtelSemConvInstrumentationLibrary=true` opts a library out), QYL0112 (a `Spans.Work` name or `SetAppTag` key that is not a constant), QYL0113 (a business key with a token the collector denies), QYL0120 (an `OpenTelemetry.Instrumentation.*` tracer extension in a compilation that calls `AddQyl`), QYL0121 (`Custom` with a value a member carries), QYL0122 (a starter inside a delegate passed to `AIFunctionFactory.Create`). QYL0110 is rewritten: a `catch` that throws inside a starter's scope without `RecordFailure`, now at Error.
- Analyzers: deleted QYL0100, QYL0101, QYL0102–QYL0109, QYL0300, QYL0302, QYL0303, QYL0405, QYL0500 and QYL0501 (rules over the `[Traced]`/`[AgentTraced]`/`[Meter]` types no repository ships, service-defaults names that no longer exist, and the checks the starters replace), with their code fixes, `CodeFixResources` and rule pages.
- Analyzers: attribute writes resolve through the generated `SemconvTypedSurface` table (typed setters, value-set members, starter parameters and the attributes a starter sets), so QYL0012, QYL0400 and QYL0401 see typed code; QYL0200 accepts `qyl.app.*` keys and the `Qyl.Telemetry.Application` scope.
- Value sets are open structs: every enum attribute gets a `readonly struct` in the Activities namespace (`DbSystemName`, `ErrorType`, `HttpRequestMethod`, ...) with one static member per registry value, `Custom(...)`, `Value`, `Key` and value equality, and no conversion from `string`. The typed setters of enum attributes take the struct (`SetDbSystemName(DbSystemName.Postgresql)`). The 91 nested `*Values` classes in `Generated/Activities` are deleted; the `const string` classes under `Generated/Attributes` stay.
- No `Activity` setter for a resource-only attribute (referenced by entities and by no span, event or metric: `service.name`, `deployment.environment.name`, `host.*`, `k8s.*`, `telemetry.sdk.*`, ...); the `TelemetryActivityExtensions` and eight incubating extension classes disappear with them. No generic `SetQylApp` either.
- `scripts/generate.sh` clears both Generated trees before Weaver writes them, so an output the registry stops producing leaves no stale file.
- Fix: a `{recommended: <condition>}` requirement level maps to `Recommended`, not `ConditionallyRequired` (for example `network.peer.address` on `db.client`).
- Registry: the `qyl.app` template attribute (business keys, `qyl.app.<key>`), the `qyl.instrumentation.domain` values `app`, `messaging.process`, `gen_ai.invoke_agent` and `gen_ai.execute_tool`, and the scope `Qyl.Telemetry.Application` (`QylTelemetryNames.Scopes.QylTelemetryApplication`).
- `SchemaUrl.Qyl` and `SchemaUrl.QylVersion`: the qyl registry's schema URL, `https://qyl.at/schemas/10.0.0`.
- Vendor descriptions that contradicted the library sources at the pinned tags: `messaging.masstransit.consumer_type` (a namespace-qualified `TypeCache<T>.ShortName`, a state machine's `GetType().Name` or `Handler`), `messaging.masstransit.message_types` (message type URNs), `peer.address` (MassTransit's diagnostic address, not a network address), `elastic.transport.schema_url` (set only when the client's schema differs from the product's), `soap.message_version` (the full `MessageVersion` display string), `quartz.job.type` (namespace-qualified, not assembly-qualified), `message.source_id`/`message.target_id` (which edge runners set them), and `nservicebus.is_deferred`, `.originating_address`, `.non_durable`, `.databus.content_type` (promoted from headers NServiceBus.Core 10.2.9 never sets itself; they stay because other packages and older senders set them).
- The release smoke (`eng/release/verify-packages.sh`) exercises the value-set setters and a starter on the application scope; README and package READMEs describe the new surface.
- Give every string attribute of the qyl and vendor registries examples taken from the code that writes it, and declare the manifest's `file_format`; Weaver no longer reports diagnostics for this registry.
- Build: the pipeline tests run under Microsoft code coverage, and `./build.sh Coverage` (in `mise run check-full`) gates line and branch coverage of this repository's sources through DotCov.Fallout 4.1.0.

## [9.9.2] - 2026-10-14

- Move ANcpLua.Roslyn.Utilities from 2.2.47 to 2.2.48: the analyzers embed `ANcpLua.Roslyn.Utilities.Sources` 2.2.48.
- Replace the copied analyzer test harness with `ANcpLua.Roslyn.Utilities.Testing` 2.2.48 (`Compile`, `DictionaryAnalyzerConfigOptionsProvider`, `SourceFixture`, `RepositoryRoot`); the tests and their assertions are unchanged.
- No public API, analyzer rule or generated vocabulary change.
