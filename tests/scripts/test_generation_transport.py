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
    def test_large_policy_reaches_every_weaver_target_without_argv_payloads(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / 'generation with spaces'
            scripts = root / 'scripts'
            shutil.copytree(ROOT / 'scripts', scripts, ignore=shutil.ignore_patterns('__pycache__'))
            for name in ('fetch-core.sh', 'generate-reference.sh'):
                (scripts / name).write_text('#!/usr/bin/env bash\nexit 0\n')
                (scripts / name).chmod(0o755)
            (root / '.build').mkdir()
            (root / '.build/core-ref.txt').write_text('v1.0.0\n')
            (root / '.build/core-commit.txt').write_text('core-commit\n')
            (root / 'Version.props').write_text('<Project><WeaverVersion>0.28.0</WeaverVersion></Project>')
            (root / 'Directory.Build.props').write_text('<Project><VersionPrefix>1.0.0</VersionPrefix></Project>')
            (root / 'LICENSE').write_text('test license\n')
            (root / 'typescript').mkdir()
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
    Path('.build/received-policy-' + args[-2] + '.json').write_text(json.dumps(params['policy']))
    if args[-2] == 'snapshot':
        enum = {'members': [{'id': 'one', 'value': 'one'}]}
        Path('.build/registry.json').write_text(json.dumps({
            'schema_url': 'https://qyl.at/schemas/1.0.0',
            'registry': {'attributes': [{'key': 'secret.kind', 'type': enum},
                                        {'key': 'secret.text', 'type': 'string'},
                                        {'key': 'plain.kind', 'type': enum}]},
            'dependencies': {'core': {'registry': {'attributes': [{'key': 'secret.level', 'type': enum},
                                                                  {'key': 'secret.text', 'type': enum}]}}},
        }))
''')
            (binaries / 'weaver').chmod(0o755)
            env = dict(os.environ, PATH=str(binaries) + os.pathsep + os.environ['PATH'], WEAVER='weaver')
            run = subprocess.run(['bash', str(scripts / 'generate.sh'), '--verbose'], cwd=root,
                                 env=env, capture_output=True, text=True)
            self.assertEqual(0, run.returncode, run.stdout + run.stderr)
            received = {target: json.loads((root / ('.build/received-policy-' + target + '.json')).read_text())
                        for target in ('snapshot', 'csharp', 'typescript')}
            # The snapshot resolves the attribute types; the targets after it receive the enum keys
            # that contain a denied token as exemptions. A top-level string definition stays denied.
            self.assertEqual(['allowed.key'], received['snapshot']['token_exempt'])
            self.assertEqual(received['csharp'], received['typescript'])
            policy = received['typescript']
            self.assertGreater(len(json.dumps(policy).encode()), 131072)
            self.assertEqual(policy, {
                'vendor_keys': ['vendor.key'], 'not_persisted': ['secret.key'],
                'not_persisted_prefixes': [], 'denied_key_tokens': ['secret'],
                'token_exempt': ['allowed.key', 'secret.kind', 'secret.level'], 'exception_text': [],
                'captured_fields': captured, 'phase_end': ['end.key'],
                'spans': {'test.span': {'starter': True, 'domain': 'test',
                                      'fixed': {'test.key': 'test.value'}, 'templates': ['test {name}'],
                                      'substitutions': {'name': 'test.key'}}},
            })
            calls = [json.loads(line) for line in (root / '.build/weaver-calls.jsonl').read_text().splitlines()]
            generations = [args for args in calls if args[:2] == ['registry', 'generate']]
            self.assertEqual(['snapshot', 'csharp', 'typescript'], [args[-2] for args in generations])
            for args in generations:
                self.assertIn('--params', args)
            fingerprints = [[value for value in args if value.startswith('registry_snapshot_sha256=')]
                            for args in generations[1:]]
            self.assertEqual(fingerprints[0], fingerprints[1])
            self.assertRegex(fingerprints[0][0], '^registry_snapshot_sha256=[0-9a-f]{64}$')
            self.assertEqual('test license\n', (root / 'typescript/LICENSE').read_text())
            self.assertTrue(any(args[:2] == ['registry', 'check'] and '--v2' in args
                                and 'registry/policies-v2' in args for args in calls))
