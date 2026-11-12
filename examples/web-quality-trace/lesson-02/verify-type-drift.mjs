import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { join } from "node:path";

const root = fileURLToPath(new URL("./", import.meta.url));
mkdirSync(join(root, ".local"), { recursive: true });
const fixture = mkdtempSync(join(root, ".local", "type-drift-"));
const generated = readFileSync(join(root, "src/generated-query.d.ts"), "utf8");
const marker = "InspectionSummary: {";
assert.equal(generated.split(marker).length, 2);
writeFileSync(join(fixture, "generated-query.d.ts"),
  generated.replace(marker, marker + "\n            requiredByServer: string;"));
writeFileSync(join(fixture, "inspection-schema.ts"), readFileSync(join(root, "src/inspection-schema.ts")));

let rejected = false;
try {
  execFileSync(process.execPath, [join(root, "node_modules/typescript/bin/tsc"),
    "--noEmit", "--strict", "--skipLibCheck", "--target", "ES2022",
    "--module", "ESNext", "--moduleResolution", "Bundler", join(fixture, "inspection-schema.ts")],
  { cwd: root, stdio: "pipe", encoding: "utf8" });
} catch (error) {
  assert.match(String(error.stdout), /requiredByServer/);
  rejected = true;
}
assert.ok(rejected, "A required server field must cause a compile error.");
console.log("PASS: an added required server field fails schema compatibility checking.");
