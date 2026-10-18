#!/usr/bin/env python3
"""Compare packed JS exports and generated C# with the resolved Weaver registry."""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
snapshot = json.loads((ROOT / '.build/typescript-registry.json').read_text())
actual = json.loads(Path(sys.argv[1]).read_text())

def symbol(value):
    return re.sub(r'[^A-Z0-9]+', '_', value.upper()).strip('_')

def rows(kind, key):
    merged = {}
    for registry in [*[d['registry'] for d in snapshot['dependencies'].values()], snapshot['registry']]:
        merged.update((r[key], r) for r in registry.get(kind, []))
    return list(merged.values())

def included(row, tier):
    return not row.get('annotations', {}).get('code_generation', {}).get('exclude', False) and (tier == 'incubating' or row.get('stability') == 'stable' or row.get('deprecated') is not None)

sources = {}
for kind in ('Attributes', 'Metrics', 'Events'):
    sources[kind] = '\n'.join(p.read_text() for package in ('Qyl.Telemetry.SemanticConventions.Incubating', 'Qyl.Telemetry.SemanticConventions') for p in (ROOT / 'src' / package / 'Generated' / kind).glob('*.cs'))

for tier in ('stable', 'incubating'):
    expected = {'SCHEMA_URL': snapshot['schema_url'], 'REGISTRY_SNAPSHOT_SHA256': actual[tier]['REGISTRY_SNAPSHOT_SHA256']}
    dotted_symbols = {symbol(r['key']) for r in rows('attributes', 'key') if included(r, tier) and '.' in r['key']}
    for row in rows('attributes', 'key'):
        if not included(row, tier):
            continue
        key = row['key']
        constant = 'ATTR_' + symbol(key)
        if '.' not in key and symbol(key) in dotted_symbols:
            constant += '_NATIVE'
        assert constant not in expected, f'duplicate projected attribute constant: {constant}'
        expected[constant] = key
        # The attribute and its values must share the same C# block, not merely occur elsewhere.
        block = re.search(r'^    public const string \w+ = ' + re.escape(json.dumps(key)) + r';(.*?)(?=^    public const string|\Z)', sources['Attributes'], re.S | re.M)
        assert block, f'C# attribute missing: {key}'
        if isinstance(row['type'], dict):
            for member in row['type'].get('members', []):
                effective = dict(member, stability=member.get('stability', row.get('stability')))
                if included(effective, tier):
                    expected[symbol(key) + '_VALUE_' + symbol(member['id'])] = member['value']
                    literal = json.dumps(member['value'])
                    cs_type = 'string' if isinstance(member['value'], str) else 'long'
                    assert re.search(r'public const ' + cs_type + r' \w+ = ' + re.escape(literal) + ';', block[1]), f'C# enum missing: {key}/{member["id"]}'
    for row in rows('metrics', 'name'):
        if included(row, tier):
            key = 'METRIC_' + symbol(row['name'])
            expected[key] = row['name']
            expected[key + '_UNIT'] = row.get('unit', '')
            expected[key + '_DEFINITION'] = {k: row.get(k, '' if k == 'unit' else 'development') for k in ('name', 'unit', 'instrument', 'stability')}
            instrument = {'counter': 'Counter', 'updowncounter': 'UpDownCounter', 'gauge': 'Gauge', 'histogram': 'Histogram'}[row['instrument']]
            pattern = r'MetricDefinition<global::Qyl.Telemetry.SemanticConventions\.' + instrument + r'> \w+ =\s+new\(\s+name: ' + re.escape(json.dumps(row['name'])) + r',\s+unit: ' + re.escape(json.dumps(row.get('unit', ''))) + ','
            assert re.search(pattern, sources['Metrics']), f'C# metric shape missing: {row["name"]}'
    for row in rows('events', 'name'):
        if included(row, tier):
            expected['EVENT_' + symbol(row['name'])] = row['name']
            assert 'name: ' + json.dumps(row['name']) + ',' in sources['Events'], f'C# event missing: {row["name"]}'
    assert actual[tier] == expected, f'{tier} packed exports differ from resolved registry: missing={expected.keys()-actual[tier].keys()}, extra={actual[tier].keys()-expected.keys()}'
fingerprint = (ROOT / 'src/Qyl.Telemetry.SemanticConventions/Generated/RegistrySnapshot.g.cs').read_text()
assert '"' + actual['stable']['REGISTRY_SNAPSHOT_SHA256'] + '"' in fingerprint
assert actual['stable']['REGISTRY_SNAPSHOT_SHA256'] == actual['incubating']['REGISTRY_SNAPSHOT_SHA256']
print('Full resolved registry parity passed: attributes, enum values, metric names/instruments/units, events, tiers and fingerprint')
