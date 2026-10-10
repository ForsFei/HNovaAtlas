import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import chokidar from "chokidar";
import { prepareContent } from "./content-pipeline";

await prepareContent(false);
const require = createRequire(import.meta.url);
const next = spawn(process.execPath, [require.resolve("next/dist/bin/next"), "dev", ...process.argv.slice(2)], { stdio: "inherit" });
const watcher = chokidar.watch("content", { ignoreInitial: true, awaitWriteFinish: { stabilityThreshold: 200, pollInterval: 50 } });
let queue = Promise.resolve();
let timer: NodeJS.Timeout | undefined;
watcher.on("all", () => {
  clearTimeout(timer);
  timer = setTimeout(() => {
    queue = queue.then(() => prepareContent(false)).then(() => undefined).catch((error: unknown) => {
      console.error(`[content] 生成失败，请修复内容后保存：${String(error)}`);
      // Keep the last valid index available until the content is fixed.
    });
  }, 250);
});
async function close() { clearTimeout(timer); await watcher.close(); next.kill(); }
process.on("SIGINT", () => void close());
process.on("SIGTERM", () => void close());
next.on("exit", (code) => { void watcher.close().then(() => process.exit(code ?? 0)); });
