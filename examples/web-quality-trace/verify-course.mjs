import { spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { performance } from "node:perf_hooks";

const modes = new Set(["--all", "--api", "--browser"]);
const args = process.argv.slice(2);
if (args.length > 1 || (args.length === 1 && !modes.has(args[0]))) {
  console.error("Usage: npm run verify:trace -- [--all|--api|--browser]");
  process.exit(2);
}
if (Number(process.versions.node.split(".")[0]) < 24) {
  console.error("Use Node.js 24 or later for the course SQLite checks.");
  process.exit(2);
}
const mode = args[0] ?? "--all";
const root = fileURLToPath(new URL("../../", import.meta.url));
const frontend = fileURLToPath(new URL("./lesson-02/", import.meta.url));
const npm = process.env.npm_execpath;
if (mode !== "--api" && (!npm || !existsSync(new URL("./lesson-02/node_modules/vite/package.json", import.meta.url)))) {
  console.error("Run through npm run verify:trace, after npm ci in examples/web-quality-trace/lesson-02.");
  process.exit(2);
}

const started = performance.now();
let completed = 0;
function run(label, executable, arguments_, cwd = root) {
  console.log("\n[trace] " + label);
  const result = spawnSync(executable, arguments_, { cwd, stdio: "inherit", windowsHide: true });
  if (result.error || result.status !== 0) {
    console.error("[trace] FAILED " + label);
    if (result.error) console.error(result.error.message);
    process.exit(result.status ?? 1);
  }
  completed++;
}
function frontendTask(name) {
  run(name, process.execPath, [npm, "run", name], frontend);
}

run("Build ASP.NET Core example", "dotnet", ["build", "examples/web-quality-trace/lesson-03", "--nologo"]);
if (mode !== "--browser") {
  run("Test server startup fault injection", process.execPath, ["examples/web-quality-trace/lesson-03/verify-server-startup.mjs"]);
  run("API scenarios with independent fixtures", process.execPath, [
    "examples/web-quality-trace/lesson-03/verify.mjs",
    "--review", "--events", "--mes", "--range", "--csv", "--diagnostics",
    "--contract", "--backup", "--migrate", "--cursor-pages",
    "--image-cache", "--evidence-download", "--read-limit", "--review-filter",
  ]);
  run("Composite index experiment", process.execPath, ["examples/web-quality-trace/lesson-03/explain-inspection-query.mjs"]);
  run("Cursor paging experiment", process.execPath, ["examples/web-quality-trace/lesson-03/compare-inspection-pagination.mjs"]);
}
if (mode !== "--api") {
  frontendTask("check:contract");
  frontendTask("test:type-drift");
  frontendTask("test:e2e");
  frontendTask("test:e2e:live");
}
console.log(`\n[trace] PASS ${mode}: ${completed} stages in ${((performance.now() - started) / 1000).toFixed(1)}s.`);
