import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]


class GenerationTransportTests(unittest.TestCase):
    def test_large_policy_reaches_weaver_and_typescript_without_argv_payloads(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / 'generation with spaces'
            scripts = root / 'scripts'
            scripts.mkdir(parents=True)
            for name in ('generate.sh', 'generate-typescript.py'):
                shutil.copyfile(ROOT / 'scripts' / name, scripts / name)
            for name in ('fetch-core.sh', 'generate-reference.sh'):
                (scripts / name).write_text('#!/usr/bin/env bash\nexit 0\n')
                (scripts / name).chmod(0o755)
            (root / '.build').mkdir()
            (root / '.build/core-ref.txt').write_text('v1.0.0\n')
            (root / '.build/core-commit.txt').write_text('core-commit\n')
            (root / 'Version.props').write_text('<Project><WeaverVersion>0.28.0</WeaverVersion></Project>')
            (root / 'Directory.Build.props').write_text('<Project><VersionPrefix>1.0.0</VersionPrefix></Project>')
            (root / 'LICENSE').write_text('test license\n')
            (root / 'src/Qyl.Telemetry.SemanticConventions/Generated').mkdir(parents=True)
            (root / 'registry/qyl').mkdir(parents=True)
            (root / 'registry/vendor').mkdir()
            (root / 'registry/manifest.yaml').write_text(yaml.safe_dump({
                'schema_url': 'https://qyl.at/schemas/1.0.0',
                'dependencies': [{'name': 'genai', 'registry_path': 'https://example.org/genai.git@commit[model]'}],
            }))
            (root / 'registry/qyl/names.yaml').write_text(yaml.safe_dump({'groups': [{
                'annotations': {'qyl': {'scope_names': ['qyl.test']}},
            }]}))
            (root / 'registry/vendor/test.yaml').write_text(yaml.safe_dump({'groups': [{
                'attributes': [{'id': 'vendor.key'}],
                'annotations': {'qyl': {'vendor': {'activity_sources': [{'name': 'Vendor.Test'}]}}},
            }]}))
            captured = {
                'http_roots': ['custom.' + 'x' * 150000], 'rpc_roots': ['custom.metadata'],
                'http_name_characters': 'ab+', 'rpc_name_characters': 'ab',
            }
            (root / 'registry/qyl/policy.yaml').write_text(yaml.safe_dump({'groups': [
                {'id': 'qyl.policy.persistence',
                 'attributes': [{'ref': 'secret.key', 'annotations': {'qyl': {'persisted': False}}},
                                {'ref': 'allowed.key', 'annotations': {'qyl': {'token_exempt': True}}}],
                 'annotations': {'qyl': {'denied_key_tokens': ['secret'], 'captured_fields': captured}}},
                {'id': 'qyl.policy.phase',
                 'attributes': [{'ref': 'end.key', 'annotations': {'qyl': {'phase': 'end'}}}]},
                {'id': 'qyl.policy.spans', 'annotations': {'qyl': {'spans': {
                    'test.span': {'starter': True, 'domain': 'test', 'fixed': {'test.key': 'test.value'},
                                  'span_name': {'templates': ['test {name}'], 'substitutions': {'name': 'test.key'}}},
                }}}},
            ]}))
            binaries = root / 'bin'
            binaries.mkdir()
            (binaries / 'uv').write_text('#!/usr/bin/env bash\nshift 6\nexec python3 "$@"\n')
            (binaries / 'uv').chmod(0o755)
            (binaries / 'weaver').write_text('''#!/usr/bin/env python3
import json
import sys
from pathlib import Path
import yaml

args = sys.argv[1:]
if args == ['--version']:
    print('weaver 0.28.0')
    sys.exit(0)
with open('.build/weaver-calls.jsonl', 'a') as calls:
    calls.write(json.dumps(args) + '\\n')
if args[:2] == ['registry', 'generate']:
    params = yaml.safe_load(Path(args[args.index('--params') + 1]).read_text())['params']
    Path('.build/received-policy.json').write_text(json.dumps(params['policy']))
    Path('.build/typescript-registry.json').write_text(json.dumps({
        'schema_url': 'https://qyl.at/schemas/1.0.0', 'dependencies': {}, 'registry': {},
    }))
    Path('.build/registry-catalog.json').write_text('{"attributes":[],"metrics":[]}')
    Path('src/Qyl.Telemetry.SemanticConventions/Generated').mkdir(parents=True)
''')
            (binaries / 'weaver').chmod(0o755)
            env = dict(os.environ, PATH=str(binaries) + os.pathsep + os.environ['PATH'], WEAVER='weaver')
            subprocess.run(['bash', str(scripts / 'generate.sh'), '--verbose'], cwd=root,
                           env=env, check=True, capture_output=True, text=True)
            policy = json.loads((root / '.build/received-policy.json').read_text())
            self.assertGreater(len(json.dumps(policy).encode()), 131072)
            self.assertEqual(policy, {
                'vendor_keys': ['vendor.key'], 'not_persisted': ['secret.key'],
                'not_persisted_prefixes': [], 'denied_key_tokens': ['secret'],
                'token_exempt': ['allowed.key'], 'exception_text': [],
                'captured_fields': captured, 'phase_end': ['end.key'],
                'spans': {'test.span': {'starter': True, 'domain': 'test',
                                      'fixed': {'test.key': 'test.value'}, 'templates': ['test {name}'],
                                      'substitutions': {'name': 'test.key'}}},
            })
            for relative in ('typescript/src/policy.ts', 'typescript/dist/policy.js'):
                self.assertIn(json.dumps(captured['http_roots']), (root / relative).read_text())
            calls = [json.loads(line) for line in (root / '.build/weaver-calls.jsonl').read_text().splitlines()]
            self.assertEqual([args[:2] for args in calls],
                             [['registry', 'check'], ['registry', 'check'], ['registry', 'generate']])
            self.assertIn('--v2', calls[1])
            self.assertIn('registry/policies-v2', calls[1])
