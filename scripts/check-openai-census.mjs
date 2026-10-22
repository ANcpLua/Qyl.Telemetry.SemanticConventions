// Checks the audited OpenAI Codex source census against the resolved registry and the evidence
// catalogs. Reads .build/registry.json, which scripts/generate.sh writes (the gate's
// check-generated step runs it first).
import { readFileSync, existsSync } from 'node:fs';
import assert from 'node:assert/strict';
const path = name => new URL('../' + name, import.meta.url);
const json = name => JSON.parse(readFileSync(path(name), 'utf8'));
assert(existsSync(path('.build/registry.json')), 'Missing .build/registry.json: run scripts/generate.sh first');
const registry = json('.build/registry.json').registry;
const expected = json('docs/evidence/openai-runtime-census.json');
const evidence = json('docs/evidence/openai-reference.json').params.openai_reference;

// The native OpenAI signals the registry defines, keyed by the name they travel under.
const openai = group => group.annotations?.qyl?.openai;
const wireName = attribute => attribute.annotations?.qyl?.openai?.literal_name ?? attribute.key;
const native = (kind, recordKind) => registry[kind].filter(group => openai(group)?.record_kind === recordKind);
const nativeSignals = {
  events: [native('events', 'native_event'), group => group.name],
  metrics: [native('metrics', 'native_metric'), group => group.name],
  spans: [native('spans', 'native_span'), group => openai(group).literal_name],
};
const actual = {};
for (const [domain, [groups, nameOf]] of Object.entries(nativeSignals)) {
  const attributes = new Map();
  for (const group of groups)
    for (const attribute of group.attributes ?? []) {
      const name = nameOf(group);
      if (!attributes.has(name)) attributes.set(name, new Set());
      attributes.get(name).add(wireName(attribute));
    }
  actual[domain] = { names: [...new Set(groups.map(nameOf))], attributes };
}

for (const domain of ['events', 'metrics', 'spans'])
  assert.deepEqual(actual[domain].names.toSorted(), expected[domain].toSorted(), `${domain}: source census differs from registry`);
const controls = new Set(['self', 'telemetry', 'event.name', 'otel.kind', 'otel.name']);
for (const domain of ['events', 'metrics', 'spans']) {
  for (const [name, records] of Object.entries(expected.source_records[domain])) {
    const fields = new Set(records.flatMap(r => domain === 'metrics' ? r.tags :
      Object.keys({ ...r.fields, ...r.modes?.common, ...r.modes?.log, ...r.modes?.trace })));
    for (const key of fields)
      if (!controls.has(key)) assert(actual[domain].attributes.get(name)?.has(key), `${name}: missing native field ${key}`);
  }
}

// Every audited name is a registered native signal or is listed in an evidence catalog, and every
// evidence catalog section lists each field the source census found for it.
const blockText = block => block.kind === 'table'
  ? [block.headers, ...block.rows].map(row => '| ' + row.join(' | ') + ' |').join('\n')
  : block.text;
const catalogs = [...evidence.sections, ...evidence.native_supplements]
  .map(section => `## ${section.title}\n\n${section.blocks.map(blockText).join('\n\n')}\n`).join('\n');
const registered = new Set(Object.values(actual).flatMap(domain => domain.names));
for (const [domain, names] of Object.entries(evidence.census)) {
  if (!Array.isArray(names)) continue;
  for (const name of names) {
    const shortName = name.replace(/^protobuf_analytics_events\.v1\./, '');
    assert(registered.has(name) || catalogs.includes(name) || catalogs.includes(shortName), `${domain}: missing evidence entry ${name}`);
  }
}
for (const entry of expected.evidence_fields) {
  const marker = '\n' + '#'.repeat(entry.level) + ' ' + entry.heading + '\n';
  const start = catalogs.indexOf(marker);
  assert(start >= 0, `${entry.catalog}: missing section ${entry.heading}`);
  const remaining = catalogs.slice(start + marker.length);
  const end = remaining.search(new RegExp(`^#{1,${entry.level}} `, 'm'));
  const body = end < 0 ? remaining : remaining.slice(0, end);
  for (const field of entry.fields)
    assert(body.includes('`' + field + '`'), `${entry.catalog}: ${entry.heading} omits ${field}`);
}
assert(evidence.sections.some(section => section.title.includes('26.1013.40934')), 'Missing the ChatGPT desktop 26.1013.40934 catalog');
assert(!actual.events.names.includes('codex.browser_use.security_check'), 'An inactive Chrome hook became a native event');
console.log(`PASS: ${actual.events.names.length} OpenAI event names, ${actual.metrics.names.length} metrics, ${actual.spans.names.length} span names, native fields and every evidence catalog are covered.`);
