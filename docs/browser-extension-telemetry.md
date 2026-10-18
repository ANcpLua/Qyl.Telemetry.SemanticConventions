# Browser extension telemetry coverage

This audit distinguishes source declarations from telemetry that the standalone extension can emit. It does not infer a customer OTLP schema from product analytics.

## Claude for Chrome 1.0.101

Extension `fcoeoabgfenejglbffodgkkbkcdhcgfn` contains OpenTelemetry API calls under the `claude-browser-extension` tracer scope. The five span contracts are in `registry/vendor/anthropic-claude-chrome.yaml`:

- `message_streaming`
- `send_user_message`
- `retry_last_message`
- `anthropic_message_create` (default; the caller can override it)
- `tool_execution_{tool_name}_{action}`, falling back to `tool_execution_{tool_name}`

The inspected bundle contains no standalone TracerProvider registration or OTLP exporter. These declarations require an externally registered provider to record spans. The separate `/api/event_logging/v2/batch` product-analytics transport is not an OTLP endpoint.

## ChatGPT for Chrome 1.26.1005.11759

Extension `hehggadaopoacecdllhhajmbjkdcmajg`; inspected all 18 JavaScript files (1,969,664 bytes). Sorted file-hash-list SHA-256: `3ae2c44d1b54df50f858a9c3c618d9ca5d229838394cdbae8a19dfad150076cd`. Full source evidence is in `docs/chatgpt-chrome-telemetry.json`.

The standalone runtime uses explicit no-op performance, command-timing and error-reporting adapters. It supplies no `runtime.otel` logger. No OpenTelemetry SDK, tracer-provider registration or OTLP exporter was found. Consequently, the following bundled declarations are documented as inactive hooks, not registered as telemetry emitted by this standalone extension. A host runtime may have its own separately configured telemetry.

### Bundled span hooks

| Name | Attribute fields |
| --- | --- |
| `browser_use.cdp.execute` | `browser_use.cdp.domain`, `browser_use.cdp.method`, `browser_use.tab.id`, `tab.id`, `browser_use.cdp.eval.await_promise`, `browser_use.cdp.eval.return_by_value`, `browser_use.cdp.eval.kind`, `browser_use.playwright.operation`, `browser_use.playwright.phase` |
| `browser_use.tab.reclaim` | `browser_use.command.type`, `browser_use.tab.id` |
| `browser_use.tab.handoff` | `browser_use.command.type`, `browser_use.tab.id`, `browser_use.tab.mark_status` |

`withCommandTelemetry(commandType, params, callback)` is also a no-op in this adapter. It does not establish a concrete emitted span name or kind. `browser_use.cdp.target_type` appears on an error-reporting tag path; the standalone error reporter is a no-op.

### Bundled security event

`codex.browser_use.security_check` is guarded by `runtime.otel != null`. Its schema is retained here because it is present in the inspected source, although the standalone adapter does not provide that logger.

| Field | Meaning / source type |
| --- | --- |
| `schema_version` | integer, fixed 1 |
| `permission` | string |
| `decision` | allowed / denied |
| `decision_context` | evaluated / not_required / error / cached / cancelled |
| `decided_by` | automated_review / user / saved_user_permission / history_policy / network_policy / builtin_rule / openai_site_policy / security_mode / unknown |
| `policy` | producer-supplied policy metadata |
| `reason` | string when present |
| `origin` | string when present |
| `connector_id` | string when present |
| `tool_name` | string when present |
| `backend` | string when present |
| `duration_ms` | number when present |

Permission values: `browser_auth`, `browser_history`, `origin_access`, `file_download`, `file_upload`, `full_cdp_access`, `page_asset_cross_origin_fetch`, `page_asset_download`, `raw_cdp_destination`, `webmcp_access`.

Successful navigation-policy and site-status checks are omitted by the serializer; denials are retained. An approval cancellation uses `decision_context=cancelled`; cached user or automated-review decisions use `cached`.

### Statsig product analytics

Background analytics are configured for `https://ab.chatgpt.com/v1`. Background records add `build_channel`, `extension_version`, and `browser_family`. Runtime records add `browserClientBuild`, `codexAppVersion`, and `browser_family`; browser-aware calls also add `backend`. The Statsig user envelope may include account attribution and a local stable identifier. These are vendor product-analytics records, not customer OTLP log events. Feature gates and runtime initialization still control whether a source call is delivered.

| Event | Direct source metadata fields |
| --- | --- |
| `codex_browser_auth_attempt` | `dropped_attempt_events`, `field_index`, `method`, `origin_relation`, `outcome`, `phase`, `reinspection`, `request_class`, `request_id`, `sequence`, `submission_mode` |
| `codex_browser_auth_save_preflight` | `submission_mode` |
| `codex_browser_auth_submission_failed` | `first_outcome`, `first_reason`, `first_request_class`, `first_submission_phase`, `request_id`, `submission_mode`, `terminal_outcome`, `terminal_reason`, `terminal_request_class`, `terminal_submission_phase` |
| `codex_browser_auth_submission_rejected` | `first_outcome`, `first_reason`, `first_request_class`, `first_submission_phase`, `request_id`, `submission_mode`, `terminal_outcome`, `terminal_reason`, `terminal_request_class`, `terminal_submission_phase` |
| `codex_browser_use_backend_session_started` | `platform` |
| `codex_browser_use_bot_detection_reported` | `conversation_id`, `hostname`, `reason`, `session_id`, `thread_id`, `thread_source`, `turn_id` |
| `codex_browser_use_invocation_ready` | `backend`, `platform`, `release` |
| `codex_browser_use_invocation_started` | `backend`, `platform`, `release` |
| `codex_browser_use_page_asset_bundle_failed` | `assetKind`, `contentType`, `failureType` |
| `codex_browser_use_response_meta_screenshot_failed` | `backend`, `commandType`, `phase` |
| `codex_browser_use_response_meta_tabs_failed` | `backend`, `commandType`, `phase` |
| `codex_browser_use_security_blocked` | `backend`, `command_type`, `thread_id`, `turn_id` |
| `codex_browser_use_setup` | `backend` |
| `codex_chrome_extension_browser_tunnel` | `observed_at_ms`, `worker_id` |
| `codex_chrome_extension_sidechat_open` | `destination`, `source` |
| `codex_chrome_extension_toolbar_state` | `trigger` |
| `codex_chrome_extension_work_sidepanel_load` | `attempt`, `chromium_major`, `elapsed_ms`, `extension_id`, `timed_out` |
| `codex_webmcp_disabled` | `descriptor_bytes`, `registration_changes`, `tool_count` |

The browser-tunnel helper accepts open-ended metadata, converts defined values to strings, omits `instance_id`, and adds `observed_at_ms` and `worker_id`. It records `polling` and `setup` as event values. The command-timing callback has a dynamic event name; the standalone timing adapter does not invoke it. Statsig SDK diagnostics and exposure events are SDK-owned, not an additional published OpenAI semantic-convention registry.

### Sources and interpretation

- [Claude extension listing](https://chromewebstore.google.com/detail/claude/fcoeoabgfenejglbffodgkkbkcdhcgfn)
- [ChatGPT extension listing](https://chromewebstore.google.com/detail/chatgpt/hehggadaopoacecdllhhajmbjkdcmajg)
- [OpenAI browser extension documentation](https://learn.chatgpt.com/docs/chrome-extension)
- [OpenAI local-security telemetry boundary](https://learn.chatgpt.com/docs/enterprise/chatgpt-work-local-security)

Source positions are JavaScript UTF-16 offsets. OpenAI documents host-runtime browser capability-check telemetry in supported versions; that does not turn the standalone extension's no-op adapter into an exporter. No live browser tasks or telemetry settings were changed for this audit.
