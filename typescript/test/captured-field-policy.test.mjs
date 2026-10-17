import assert from 'node:assert/strict';
import { test } from 'node:test';
import { getCapturedFieldPersistence } from '../dist/policy.js';

for (const root of ['http.request.header', 'http.response.header', 'rpc.request.metadata', 'rpc.response.metadata']) {
  test(`${root} preserves selected fields and enforces the protocol alphabet`, () => {
    for (const name of ['authorization', 'cookie', 'set-cookie', 'x-api-key', 'x-token-count', 'mcp-param-secret', 'trace-bin', 'x_request.id']) {
      assert.equal(getCapturedFieldPersistence(`${root}.${name}`), true);
      assert.equal(getCapturedFieldPersistence(`${root}.${name}`.toUpperCase()), true);
    }
    for (const key of [root, root.toUpperCase(), `${root}.`, `${root}.bad name`, `${root}.naïve`, `${root}.Key`, `${root}.ſecret`, `${root}.😀`]) {
      assert.equal(getCapturedFieldPersistence(key), false, key);
    }
    // RFC 9110 tchar versus the gRPC custom-metadata alphabet, including controls and DEL.
    for (let code = 0; code < 128; code++) {
      const char = String.fromCharCode(code);
      const expected = /[a-zA-Z0-9_.-]/.test(char) || (root.startsWith('http.') && "!#$%&'*+^`|~".includes(char));
      assert.equal(getCapturedFieldPersistence(`${root}.x${char}y`), expected, `ASCII ${code}`);
    }
  });
}

test('nearby and embedded roots do not classify as captured fields', () => {
  for (const key of ['', 'authorization', 'http.request.headers.authorization', 'rpc.request.metadatas.key',
    'custom.http.request.header.authorization', 'user.http.response.header.cookie', 'http.requeſt.header.key']) {
    assert.equal(getCapturedFieldPersistence(key), undefined, key);
  }
});
