# Anthropic telemetry coverage

The vendor registry records native producer vocabulary. It preserves Anthropic's emitted
keys, including unprefixed fields. Shared OpenTelemetry definitions remain references to
the pinned upstream registry.

| Producer | Evidence boundary | Signals |
| --- | --- | --- |
| Claude Code 2.1.302 | Installed native bundle and monitoring reference | Metrics, logs, beta spans and span events; Agent SDK sessions use this engine |
| Claude Desktop 2.42304.0 / Cowork | Installed `app.asar`, Cowork monitoring and third-party deployment references | Cowork event schemas, desktop log forwarding and session-engine telemetry |
| Claude API / TypeScript SDK 0.135.0 | Public API span reference and published SDK source | API server spans, SDK client/internal spans, resource attribution and content metadata |
| Claude for Chrome 1.0.101 | Installed extension `fcoeoabgfenejglbffodgkkbkcdhcgfn` | OpenTelemetry API span instrumentation; no standalone collector exporter found |

The Chrome extension's `claude_browser_extension` product analytics, sent to
`/api/event_logging/v2/batch`, are a separate transport. Bundled Sentry, language grammars,
and LSP `telemetry/event` declarations are not additional customer OTLP conventions.

`claude_code.host.<eventName>` forwards validated host events with `sdk_host.<key>` string
attributes. Desktop native-host and renderer forwarding paths can also supply event names
dynamically. The vendor metadata records these open-ended paths. The SDK's current API
methods and resource-ID keys are listed explicitly; future methods require another audit.

Official-marketplace hook plugins can attach up to 20 custom Boolean or numeric fields
to `claude_code.hook_plugin_metrics`; names match `^[a-z][a-z0-9_]{0,39}$`. This event-specific
rule does not make every matching name a known global attribute. GenAI message-part
extensions inside JSON content are also documented separately from top-level attributes.

Some keys have multiple wire representations, including numeric text on logs and numbers
on spans. These retain `any` when one narrower type would be false. Desktop preserves
scalar values, drops nulls, and serializes objects/arrays to JSON strings. An erased
JavaScript parameter type does not prove a narrower attribute type.

The catalog retains every native spelling. C# identifier collisions use the deterministic
preference for a live name and then ordinal spelling within the projection. The standard
`session.id` setter therefore keeps its meaning. TypeScript retains both constants for a
dotted/bare-name collision: `ATTR_SESSION_ID` is `session.id`, and `ATTR_SESSION_ID_NATIVE`
is the native `session_id` spelling. Colliding bare C# attribute constants similarly retain
the additional key with a `Native` suffix. Live validation accepts explicitly registered native
spellings while preserving name and namespace checks for undeclared fields.

Content gates affect field presence and values. Prompt capture does not imply that every
source captures all responses or tool payloads. Detailed beta tracing, raw-body logging,
Desktop content categories, and API content mode have independent gates. API GenAI input
tokens include cache reads/writes; Claude Code's native `input_tokens` excludes them.

Cowork sessions started on web or mobile can produce telemetry. Third-party Desktop
deployments additionally export a `claude-desktop` log stream. Ordinary web/mobile chat
has no separate published customer OTLP schema in this boundary; device labels alone
are not grounds to exclude Cowork workloads.

API and SDK spans retain distinct kinds even when they share a name.
`anthropic.messages.count_tokens` is SDK-only, and its returned input-token count is not
billable usage. The retained Text Completions client span is marked deprecated. Absence
from a documentation table alone does not establish deprecation. No private server schema
or unobserved finite event list is claimed.

Sources:

- [Claude Code monitoring](https://code.claude.com/docs/en/monitoring-usage)
- [Agent SDK observability](https://code.claude.com/docs/en/agent-sdk/observability)
- [Cowork monitoring](https://claude.com/docs/cowork/monitoring)
- [Desktop telemetry](https://claude.com/docs/third-party/claude-desktop/telemetry)
- [API span reference](https://platform.claude.com/docs/en/api/opentelemetry/spans)
- [TypeScript SDK 0.135.0](https://github.com/anthropics/anthropic-sdk-typescript/tree/sdk-v0.135.0)
- [Claude Chrome extension](https://chromewebstore.google.com/detail/claude/fcoeoabgfenejglbffodgkkbkcdhcgfn)

Regenerate the complete reference, including resolved upstream definitions, with Weaver 0.28.0
and the `.j2` templates:

```bash
mise exec -- scripts/generate-reference.sh
```
