import { performance } from "node:perf_hooks";
import { setTimeout as delay } from "node:timers/promises";

async function run() {
  const [address = "http://127.0.0.1:43111", count = "16"] = process.argv.slice(2);
  const origin = new URL(address);
  const rounds = Number(count);
  if (origin.protocol !== "http:" || origin.hostname !== "127.0.0.1" ||
      origin.username || origin.password || origin.pathname !== "/" || origin.search || origin.hash ||
      !Number.isInteger(rounds) || rounds < 1 || rounds > 40) {
    throw new Error("Use a loopback origin and 1 to 40 rounds.");
  }
  const cases = [
    { name: "list", path: () => "/api/inspections?page=1&pageSize=1", expected: 200 },
    { name: "invalid-page", path: () => "/api/inspections?page=0", expected: 400 },
    { name: "missing-cycle", path: round => "/api/inspections/metrics-demo-missing-" + round, expected: 404 },
    { name: "ready", path: () => "/health/ready", expected: 200 },
  ];
  const samples = [];
  for (let round = 1; round <= rounds; round++) {
    const batch = await Promise.all(cases.map(async sample => {
      const started = performance.now();
      const response = await fetch(new URL(sample.path(round), origin), {
        signal: AbortSignal.timeout(5000), redirect: "error",
      });
      await response.arrayBuffer();
      return {
        name: sample.name, status: response.status, expected: sample.expected,
        elapsedMs: Math.round(performance.now() - started), requestId: response.headers.get("x-request-id"),
      };
    }));
    samples.push(...batch);
    if (round < rounds) await delay(250);
  }

  const counts = {};
  for (const sample of samples) counts[sample.status] = (counts[sample.status] ?? 0) + 1;
  const mismatches = samples.filter(sample => sample.status !== sample.expected || !sample.requestId);
  console.log(JSON.stringify({
    rounds, counts, mismatches, examples: samples.slice(0, cases.length),
  }, null, 2));
  if (mismatches.length) process.exitCode = 1;
}

try { await run(); }
catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
