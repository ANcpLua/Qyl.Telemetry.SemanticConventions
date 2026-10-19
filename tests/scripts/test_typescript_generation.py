import copy
import importlib.util
import subprocess
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path

spec = importlib.util.spec_from_file_location('typescript_generator', Path(__file__).resolve().parents[2] / 'scripts/generate-typescript.py')
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)

class SnapshotTests(unittest.TestCase):
    def test_native_enum_constants_use_the_attribute_collision_mapping(self):
        def attribute(key, value):
            return {'key': key, 'stability': 'stable', 'type': {'members': [
                {'id': 'ready', 'value': value, 'stability': 'stable'}]}}
        snapshot = {'schema_url': 'https://qyl.at/schemas/1.0.0', 'dependencies': {},
                    'registry': {'attributes': [attribute('collision.kind', 'canonical'),
                                               attribute('collision_kind', 'native')],
                                 'metrics': [], 'events': []}}
        policy = {'not_persisted': [], 'not_persisted_prefixes': [], 'denied_key_tokens': [],
                  'token_exempt': [], 'vendor_keys': [], 'phase_end': [], 'captured_fields': {
                      'http_roots': [], 'rpc_roots': [], 'http_name_characters': 'ab',
                      'rpc_name_characters': 'ab'}}
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'Directory.Build.props').write_text('<Project><PropertyGroup><VersionPrefix>1.0.0</VersionPrefix></PropertyGroup></Project>')
            (root / 'LICENSE').write_text('Fixture')
            (root / 'src/Qyl.Telemetry.SemanticConventions/Generated').mkdir(parents=True)
            with patch.object(generator, 'ROOT', root):
                generator.generate(snapshot, {'attributes': [], 'metrics': []}, policy)
            subprocess.run(['node', '--input-type=module', '-e', '''
import assert from 'node:assert/strict';
for (const tier of ['index', 'incubating']) {
    const m = await import(`./typescript/dist/${tier}.js`);
    assert.equal(m.ATTR_COLLISION_KIND, 'collision.kind');
    assert.equal(m.ATTR_COLLISION_KIND_NATIVE, 'collision_kind');
    assert.equal(m.COLLISION_KIND_VALUE_READY, 'canonical');
    assert.equal(m.COLLISION_KIND_NATIVE_VALUE_READY, 'native');
}
'''], cwd=root, check=True)

    def test_native_spelling_collisions_keep_both_wire_keys(self):
        names = generator.attribute_symbols([
            {'key': 'session_id'}, {'key': 'session.id'}, {'key': 'model'},
            {'key': 'mcp.server.name'}, {'key': 'mcp_server.name'},
            {'key': 'executor.registration_id'}, {'key': 'executor_registration_id'},
        ])
        self.assertEqual(names, {
            'session.id': 'SESSION_ID', 'session_id': 'SESSION_ID_NATIVE', 'model': 'MODEL',
            'mcp.server.name': 'MCP_SERVER_NAME', 'mcp_server.name': 'MCP_SERVER_NAME_NATIVE',
            'executor.registration_id': 'EXECUTOR_REGISTRATION_ID', 'executor_registration_id': 'EXECUTOR_REGISTRATION_ID_NATIVE',
        })

    def test_native_alias_cannot_overwrite_another_attribute(self):
        with self.assertRaisesRegex(ValueError, 'identifier collision'):
            generator.attribute_symbols([
                {'key': 'session.id'}, {'key': 'session_id'}, {'key': 'session.id.native'},
            ])

    def test_captured_classifier_uses_supplied_registry_roots_and_alphabets(self):
        policy = {'captured_fields': {
            'http_roots': ['custom.headers'], 'rpc_roots': ['custom.metadata'],
            'http_name_characters': 'ab+', 'rpc_name_characters': 'ab',
        }}
        with tempfile.TemporaryDirectory() as directory:
            out = Path(directory)
            (out / 'src').mkdir()
            (out / 'dist').mkdir()
            (out / 'package.json').write_text('{"type":"module"}')
            generator.generate_policy(policy, out, 'test')
            subprocess.run(['node', '--input-type=module', '-e', '''
import assert from 'node:assert/strict';
import { getCapturedFieldPersistence as classify } from './dist/policy.js';
assert.equal(classify('CUSTOM.HEADERS.A+B'), true);
assert.equal(classify('custom.metadata.a+b'), false);
assert.equal(classify('custom.headers.c'), false);
assert.equal(classify('custom.headers'), false);
assert.equal(classify('http.request.header.a'), undefined);
'''], cwd=out, check=True)

    def test_checkout_location_does_not_change_semantic_snapshot(self):
        first = {'registry': {'attributes': [{'key': 'service.name', 'provenance': {'path': '/tmp/cache-a/model.yaml'}}]}}
        second = copy.deepcopy(first)
        second['registry']['attributes'][0]['provenance']['path'] = '/tmp/cache-b/model.yaml'
        self.assertEqual(generator.semantic_snapshot(first), generator.semantic_snapshot(second))

    def test_nested_registry_semantic_changes_are_preserved(self):
        original = {'dependencies': {'core': {'registry': {'metrics': [{'name': 'duration', 'unit': 's', 'instrument': 'histogram'}]}}}}
        for field, value in [('name', 'latency'), ('unit', 'ms'), ('instrument', 'counter')]:
            changed = copy.deepcopy(original)
            changed['dependencies']['core']['registry']['metrics'][0][field] = value
            self.assertNotEqual(generator.semantic_snapshot(original), generator.semantic_snapshot(changed))

    def test_source_url_changes_are_preserved(self):
        first = {'provenance': {'source': 'https://example.org/schema/1', 'path': 'https://example.org/repo@first/model.yaml'}}
        for field in ('source', 'path'):
            second = copy.deepcopy(first)
            second['provenance'][field] += '/changed'
            self.assertNotEqual(generator.semantic_snapshot(first), generator.semantic_snapshot(second))
