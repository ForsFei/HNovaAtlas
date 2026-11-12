import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const verifier = fileURLToPath(new URL("./verify.mjs", import.meta.url));
function run(mode) {
  const preload = `
    const original = globalThis.fetch;
    let probes = 0;
    globalThis.fetch = async (input, options) => {
      if (String(input).endsWith('/health/live')) {
        probes++;
        if (${JSON.stringify(mode)} === 'always' || (${JSON.stringify(mode)} === 'once' && probes === 1)) {
          throw new TypeError('fetch failed', { cause: new Error('bad port') });
        }
      }
      if (${JSON.stringify(mode)} === 'business' && String(input).includes('/api/inspections?')) {
        return Response.json({ total: -1, items: [] });
      }
      return original(input, options);
    };
  `;
  return spawnSync(process.execPath, ["--import", "data:text/javascript," + encodeURIComponent(preload), verifier], {
    encoding: "utf8", windowsHide: true, timeout: 90000,
  });
}

const recovered = run("once");
assert.ifError(recovered.error);
assert.equal(recovered.status, 0, recovered.stdout + recovered.stderr);
assert.equal((recovered.stderr.match(/\[test-server\]/g) ?? []).length, 1);
assert.match(recovered.stdout, /preview pixels passed/);
const exhausted = run("always");
assert.ifError(exhausted.error);
assert.notEqual(exhausted.status, 0);
assert.equal((exhausted.stderr.match(/\[test-server\]/g) ?? []).length, 3);
assert.match(exhausted.stderr, /bad port/);
assert.doesNotMatch(exhausted.stdout, /preview pixels passed/);
const business = run("business");
assert.ifError(business.error);
assert.notEqual(business.status, 0);
assert.match(business.stderr, /AssertionError/);
assert.doesNotMatch(business.stderr, /\[test-server\]/);
console.log("Startup fault injection passed: one retry recovers; three rejected ports fail; business assertions never retry.");
