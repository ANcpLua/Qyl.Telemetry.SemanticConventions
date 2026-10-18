// Checks evidence coverage and semantic boundaries; Markdown itself is rendered by Weaver.
import { readFileSync, existsSync } from 'node:fs';
import assert from 'node:assert/strict';
const json = name => JSON.parse(readFileSync(new URL('../' + name, import.meta.url), 'utf8'));
const version = readFileSync(new URL('../Version.props', import.meta.url), 'utf8').match(/<WeaverVersion>([^<]+)<\/WeaverVersion>/)?.[1];
assert(version, 'Missing Weaver version pin');
const actual = json('docs/reference/coverage.json');
const expected = json('docs/evidence/openai-runtime-census.json');
const evidence = json('docs/evidence/openai-reference.json').params.openai_reference;
for (const domain of ['events', 'metrics', 'spans'])
  assert.deepEqual(actual[domain].toSorted(), expected[domain].toSorted(), `${domain}: source census differs from registry`);
const controls = new Set(['self', 'telemetry', 'event.name', 'otel.kind', 'otel.name']);
for (const [domain, output] of [['events', 'event_attributes'], ['metrics', 'metric_attributes'], ['spans', 'span_attributes']]) {
  for (const [name, records] of Object.entries(expected.source_records[domain])) {
    const fields = new Set(records.flatMap(r => domain === 'metrics' ? r.tags :
      Object.keys({ ...r.fields, ...r.modes?.common, ...r.modes?.log, ...r.modes?.trace })));
    for (const key of fields)
      if (!controls.has(key)) assert(actual[output][name].includes(key), `${name}: missing native field ${key}`);
  }
}
const openai = readFileSync(new URL('../docs/reference/OpenAI-Telemetry-Semantic-Conventions-2026-10-18.md', import.meta.url), 'utf8');
const qyl = readFileSync(new URL('../docs/reference/qyl-semantic-conventions.md', import.meta.url), 'utf8');
for (const [domain, names] of Object.entries(evidence.census)) {
  if (!Array.isArray(names)) continue;
  for (const name of names) {
    const shortName = name.replace(/^protobuf_analytics_events\.v1\./, '');
    assert(openai.includes(name) || openai.includes(shortName), `${domain}: missing evidence entry ${name}`);
  }
}
for (const entry of expected.evidence_fields) {
  const marker = '\n' + '#'.repeat(entry.level) + ' ' + entry.heading + '\n';
  const start = openai.indexOf(marker);
  assert(start >= 0, `${entry.catalog}: missing section ${entry.heading}`);
  const remaining = openai.slice(start + marker.length);
  const end = remaining.search(new RegExp(`^#{1,${entry.level}} `, 'm'));
  const body = end < 0 ? remaining : remaining.slice(0, end);
  for (const field of entry.fields)
    assert(body.includes('`' + field + '`'), `${entry.catalog}: ${entry.heading} omits ${field}`);
}
for (const name of actual.groups) assert(qyl.includes(name), `Missing group ${name} in complete reference`);
for (const name of actual.attributes) assert(qyl.includes(name), `Missing attribute ${name} in complete reference`);
for (const text of [openai, qyl]) {
  assert(text.includes('OpenTelemetry Weaver ' + version), 'Missing pinned renderer provenance');
  assert(!text.includes('{{') && !text.includes('{%'), 'Unrendered Jinja marker');
  assert(text.includes('26.1013.40934') || text === qyl);
}
assert(!actual.events.includes('codex.browser_use.security_check'), 'An inactive Chrome hook became a native event');
assert(!existsSync(new URL('../scripts/export-registry-reference.py', import.meta.url)), 'Python Markdown renderer still exists');
console.log(`PASS: ${actual.events.length} OpenAI event names, ${actual.metrics.length} metrics, ${actual.spans.length} span names, native fields and every evidence catalog are covered.`);
