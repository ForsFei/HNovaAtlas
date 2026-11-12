import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

// Simulate a Windows checkout without rewriting a user's generated file.
const preload = `
  import fs from 'node:fs';
  import { syncBuiltinESMExports } from 'node:module';
  const original = fs.readFileSync;
  fs.readFileSync = function(path, options) {
    const result = original(path, options);
    return String(path).endsWith('generated-query.d.ts') && options === 'utf8'
      ? result.replace(/\\r?\\n/g, '\\r\\n') : result;
  };
  syncBuiltinESMExports();
`;
const result = spawnSync(process.execPath, ["--import", "data:text/javascript," + encodeURIComponent(preload),
  fileURLToPath(new URL("./generate-contract.mjs", import.meta.url)), "--check"],
{ encoding: "utf8", windowsHide: true, timeout: 90000 });
assert.ifError(result.error);
assert.equal(result.status, 0, result.stdout + result.stderr);
console.log("PASS contract comparison accepts CRLF checkout without modifying the generated file.");
