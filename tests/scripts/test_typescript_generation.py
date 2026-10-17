import copy
import importlib.util
import subprocess
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location('typescript_generator', Path(__file__).resolve().parents[2] / 'scripts/generate-typescript.py')
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)

class SnapshotTests(unittest.TestCase):
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
