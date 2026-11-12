import assert from "node:assert/strict";
import { execFileSync, spawn } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdtempSync, readdirSync, readFileSync } from "node:fs";
import { once } from "node:events";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

// Each fault scenario owns a separate fixture and database.
const scenarios = process.argv.slice(2);
if (scenarios.length > 1) {
  for (const scenario of scenarios) {
    execFileSync(process.execPath, [fileURLToPath(import.meta.url), scenario], { stdio: "inherit" });
  }
  process.exit(0);
}

const directory = mkdtempSync(join(tmpdir(), "quality-trace-lesson-03-"));
const database = join(directory, "trace.db");
const app = fileURLToPath(new URL("./bin/Debug/net10.0/QualityTrace.Storage.dll", import.meta.url));
const evidenceApp = fileURLToPath(new URL("../../industrial-station-evidence/bin/Debug/net10.0/IndustrialStation.Evidence.dll", import.meta.url));
const demo = execFileSync("dotnet", [evidenceApp, "--demo"], { encoding: "utf8" });
const demoRoot = demo.match(/retained: (.+); closed=True/i)?.[1];
assert.ok(demoRoot, demo);

const evidenceRoot = join(demoRoot, "evidence");
const bundles = readdirSync(evidenceRoot, { withFileTypes: true })
  .filter(entry => entry.isDirectory()).map(entry => entry.name);
assert.equal(bundles.length, 2);

function importBundle(bundle, station = "station-a", root = evidenceRoot) {
  return execFileSync("dotnet", [app, "--TraceDb", database,
    "--Import", join(root, bundle), "--Station", station],
  { encoding: "utf8", stdio: "pipe" });
}

for (const bundle of bundles) assert.match(importBundle(bundle), /Imported/);
assert.match(importBundle(bundles[0]), /Already imported/);
assert.throws(() => importBundle(bundles[0], "station-b"), /conflicts with the existing cycle/);

async function withServer(check, environment = {}, dbFile = database) {
  for (let attempt = 1; attempt <= 3; attempt++) {
    const server = spawn("dotnet", [app, "--TraceDb", dbFile, "--urls", "http://127.0.0.1:0"], { env: { ...process.env, ...environment } });
    const exited = once(server, "exit");
    let log = "";
    let origin;
    server.stdout.setEncoding("utf8").on("data", chunk => {
      log += chunk;
      origin = log.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/)?.[1];
    });
    server.stderr.setEncoding("utf8").on("data", chunk => { log += chunk; });
    try {
      for (let i = 0; !origin && server.exitCode === null && i < 100; i++) {
        await new Promise(resolve => setTimeout(resolve, 100));
      }
      assert.ok(origin, log);
      // Probe transport before executing any business assertion.
      try {
        const probe = await fetch(origin + "/health/live", { signal: AbortSignal.timeout(5000) });
        await probe.arrayBuffer();
        assert.equal(probe.status, 200, "Startup liveness probe failed");
      } catch (error) {
        if (error instanceof TypeError && error.cause?.message === "bad port") {
          console.warn(`[test-server] Fetch rejected ${origin}; attempt ${attempt}/3.`);
          if (attempt < 3) continue;
        }
        throw error;
      }
      await check(path => fetch(origin + path, { signal: AbortSignal.timeout(5000) }), origin, () => log);
      return;
    } finally {
      if (server.exitCode === null) server.kill();
      await exited;
    }
  }
}

const api = "/api/inspections";
let ngId;
await withServer(async get => {
  const first = await (await get(api + "?page=1&pageSize=1")).json();
  const second = await (await get(api + "?page=2&pageSize=1")).json();
  assert.equal(first.total, 2);
  assert.equal(first.items.length, 1);
  assert.equal(second.items.length, 1);
  assert.notEqual(first.items[0].id, second.items[0].id);

  const ng = await (await get(api + "?station=station-a&result=NG")).json();
  assert.equal(ng.total, 1);
  assert.equal(ng.items[0].roiMean, 75);
  ngId = ng.items[0].id;
  assert.equal((await (await get(api + "?station=station-b")).json()).total, 0);
  assert.equal((await (await get(api + "?page=3&pageSize=1")).json()).items.length, 0);
  for (const query of ["?page=0", "?pageSize=101", "?page=2147483647&pageSize=100"]) {
    assert.equal((await get(api + query)).status, 400);
  }
  assert.equal((await get(api + "/missing")).status, 404);
  assert.equal((await get(api + "/missing/image")).status, 404);
});

await withServer(async get => {
  const detail = await (await get(api + "/" + ngId)).json();
  assert.equal(detail.result, "NG");
  assert.equal(detail.station, "station-a");

  const response = await get(api + "/" + ngId + "/image");
  assert.equal(response.headers.get("content-type"), "image/bmp");
  const bmp = Buffer.from(await response.arrayBuffer());
  assert.equal(bmp.toString("ascii", 0, 2), "BM");
  const width = bmp.readInt32LE(18);
  const height = -bmp.readInt32LE(22);
  const stride = (width * 3 + 3) & ~3;
  const original = readFileSync(join(evidenceRoot, ngId, "image.pgm")).subarray(-width * height);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const offset = 54 + y * stride + x * 3;
      const level = original[y * width + x];
      assert.deepEqual([...bmp.subarray(offset, offset + 3)], [level, level, level]);
    }
  }
});

console.log("Import, duplicate/conflict, pagination, validation, restart and preview pixels passed.");
console.log("Database:", database);
console.log("Evidence:", evidenceRoot);

if (process.argv.includes("--review-filter")) {
  const { DatabaseSync } = await import("node:sqlite");
  await withServer(async get => {
    const fixture = new DatabaseSync(database);
    try {
      const copy = fixture.prepare(`
        INSERT INTO Inspections (Id, Station, ImportedTicks, Result, RoiMean, Width, Height, ImageSha256, Pgm)
        SELECT ?, Station, ImportedTicks + ?, Result, RoiMean, Width, Height, ImageSha256, Pgm
        FROM Inspections WHERE Id = ?
      `);
      copy.run("pending-one", 1n, ngId);
      copy.run("pending-two", 2n, ngId);
      fixture.prepare("INSERT INTO Reviews (InspectionId, Decision, Note, Reviewer, ReviewedAt) VALUES (?, ?, ?, ?, ?)")
        .run(ngId, "confirmed-ng", "Filter fixture", "alice", "2026-11-09T00:00:00+00:00");
    } finally {
      fixture.close();
    }
    const query = "?station=station-a&result=NG&reviewStatus=pending";
    const pending = await (await get(api + query)).json();
    assert.equal(pending.total, 2);
    assert.deepEqual(pending.items.map(row => row.id).sort(), ["pending-one", "pending-two"]);
    const reviewed = await (await get(api + "?reviewStatus=reviewed")).json();
    assert.equal(reviewed.total, 1);
    assert.equal(reviewed.items[0].id, ngId);
    assert.equal((await (await get(api + "?reviewStatus=pending&result=OK")).json()).total, 0);
    const csv = await (await get(api + query + "&format=csv")).text();
    assert.ok(csv.includes("pending-one") && csv.includes("pending-two"));
    assert.ok(!csv.includes(ngId));

    const cursorPath = "/api/inspection-pages";
    const first = await (await get(cursorPath + query + "&pageSize=1")).json();
    assert.equal(first.items[0].id, "pending-two");
    const second = await (await get(cursorPath + query + "&pageSize=1&after=" + first.nextCursor)).json();
    assert.equal(second.items[0].id, "pending-one");
    assert.equal(second.nextCursor, null);
    assert.equal((await get(cursorPath + "?station=station-a&result=NG&reviewStatus=reviewed&after=" + first.nextCursor)).status, 400);

    const all = await (await get(cursorPath + "?station=station-a&result=NG&pageSize=1")).json();
    const currentToken = Buffer.from(all.nextCursor, "base64url").toString("utf8");
    const legacyToken = currentToken.replace(',"ReviewStatus":null', "");
    assert.notEqual(legacyToken, currentToken);
    const legacy = Buffer.from(legacyToken).toString("base64url");
    const resumed = await get(cursorPath + "?station=station-a&result=NG&pageSize=1&after=" + legacy);
    assert.equal(resumed.status, 200);
    assert.equal((await resumed.json()).items[0].id, "pending-one");

    assert.equal((await get(api + "?reviewStatus=invalid")).status, 400);
    assert.equal((await get(cursorPath + "?station=station-a&result=NG&reviewStatus=invalid")).status, 400);
  }, { Review__Enabled: "true", Review__Alice: "filter-test-alice", Review__Bob: "filter-test-bob", Review__Viewer: "filter-test-viewer" });
  await withServer(async get => {
    assert.equal((await get(api + "?reviewStatus=pending")).status, 400);
    assert.equal((await get("/api/inspection-pages?station=station-a&result=NG&reviewStatus=pending")).status, 400);
  }, { Review__Enabled: "false" });
  console.log("Review filtering passed: pending/reviewed, NG-only, CSV, cursor scope, legacy cursor and disabled feature.");
}

if (process.argv.includes("--read-limit")) {
  await withServer(async get => {
    const list = await get(api + "?pageSize=1");
    assert.equal(list.status, 200);
    const limited = await get("/api/inspection-pages?station=station-a&result=NG");
    assert.equal(limited.status, 429);
    const seconds = Number(limited.headers.get("retry-after"));
    assert.ok(Number.isInteger(seconds) && seconds >= 1 && seconds <= 3);
    assert.equal(limited.headers.get("cache-control"), "no-store");
    const problem = await limited.json();
    assert.equal(problem.status, 429);
    assert.equal(problem.requestId, limited.headers.get("x-request-id"));
    assert.equal((await get("/health/ready")).status, 200);
    assert.equal((await get(api + "/" + ngId)).status, 200);

    const document = await (await get("/openapi/v1.json")).json();
    for (const path of ["/api/inspections", "/api/inspection-pages"])
      assert.ok(document.paths[path].get.responses["429"].headers["Retry-After"]);

    await new Promise(resolve => setTimeout(resolve, seconds * 1000 + 100));
    const resumed = await get("/api/inspection-pages?station=station-a&result=NG");
    assert.equal(resumed.status, 200);
    assert.equal((await resumed.json()).items[0].id, ngId);
    assert.equal((await get(api + "?format=csv")).status, 429);
  }, { ReadLimit__Enabled: "true", ReadLimit__PermitLimit: "1", ReadLimit__WindowSeconds: "3", Contract__Enabled: "true" });
  console.log("Read limiting passed: shared list/cursor/CSV budget, Retry-After, unaffected detail/health and recovery.");
}

if (process.argv.includes("--evidence-download")) {
  const { DatabaseSync } = await import("node:sqlite");
  const path = api + "/" + ngId + "/evidence";
  const original = readFileSync(join(evidenceRoot, ngId, "image.pgm"));
  const manifest = JSON.parse(readFileSync(join(evidenceRoot, ngId, "manifest.json"), "utf8"));
  const digest = createHash("sha256").update(original).digest();
  assert.equal(digest.toString("hex").toUpperCase(), manifest.ImageSha256);

  await withServer(async get => {
    const response = await get(path);
    assert.equal(response.status, 200);
    assert.equal(response.headers.get("content-type"), "application/octet-stream");
    assert.match(response.headers.get("content-disposition"), /^attachment;/);
    assert.ok(response.headers.get("content-disposition").includes(ngId + ".pgm"));
    assert.equal(response.headers.get("content-digest"), `sha-256=:${digest.toString("base64")}:`);
    assert.equal(response.headers.get("cache-control"), "no-store");
    assert.deepEqual(Buffer.from(await response.arrayBuffer()), original);

    const missing = await get(api + "/missing/evidence");
    assert.equal(missing.status, 404);
    assert.equal(missing.headers.get("content-disposition"), null);

    const document = await (await get("/openapi/v1.json")).json();
    const success = document.paths["/api/inspections/{id}/evidence"].get.responses["200"];
    assert.equal(success.content["application/octet-stream"].schema.format, "binary");
    for (const name of ["Content-Disposition", "Content-Digest", "Cache-Control"])
      assert.ok(success.headers[name]);

    const fixture = new DatabaseSync(database);
    try {
      const altered = Buffer.from(original);
      altered[altered.length - 1] ^= 1;
      fixture.prepare("UPDATE Inspections SET Pgm = ? WHERE Id = ?").run(altered, ngId);
    } finally {
      fixture.close();
    }

    const invalid = await get(path);
    assert.equal(invalid.status, 500);
    assert.equal(invalid.headers.get("content-disposition"), null);
    assert.equal(invalid.headers.get("content-digest"), null);
    const problem = await invalid.json();
    assert.equal(problem.title, "Evidence integrity check failed");
    assert.equal(problem.requestId, invalid.headers.get("x-request-id"));
    console.log(`Evidence download passed: ${original.byteLength} unchanged PGM bytes, digest, 404, corruption refusal and OpenAPI.`);
  }, { Contract__Enabled: "true" });
}

if (process.argv.includes("--image-cache")) {
  const { DatabaseSync } = await import("node:sqlite");
  const imagePath = api + "/" + ngId + "/image";
  const sha256 = bytes => createHash("sha256").update(bytes).digest("hex");

  await withServer(async (get, origin) => {
    const request = (headers, path = imagePath) => fetch(origin + path, {
      headers, signal: AbortSignal.timeout(5000),
    });
    const first = await get(imagePath);
    assert.equal(first.status, 200);
    assert.equal(first.headers.get("content-type"), "image/bmp");
    assert.equal(first.headers.get("cache-control"), "private, no-cache");
    const original = Buffer.from(await first.arrayBuffer());
    const etag = first.headers.get("etag");
    assert.equal(etag, '"' + sha256(original).toUpperCase() + '"');

    for (const value of [etag, 'W/' + etag, '"other", ' + etag, "*"]) {
      const unchanged = await request({ "If-None-Match": value });
      assert.equal(unchanged.status, 304, value);
      assert.equal(unchanged.headers.get("etag"), etag);
      assert.equal(unchanged.headers.get("cache-control"), "private, no-cache");
      assert.equal((await unchanged.arrayBuffer()).byteLength, 0);
    }
    const precondition = await request({ "If-Match": '"other"' });
    assert.equal(precondition.status, 412);
    assert.equal((await precondition.json()).status, 412);
    const missing = await request({ "If-None-Match": "*" }, api + "/missing/image");
    assert.equal(missing.status, 404);

    const fixture = new DatabaseSync(database);
    try {
      const pgm = Buffer.from(fixture.prepare("SELECT Pgm FROM Inspections WHERE Id = ?").get(ngId).Pgm);
      pgm[pgm.length - 1] ^= 1;
      fixture.prepare("UPDATE Inspections SET Pgm = ?, ImageSha256 = ? WHERE Id = ?")
        .run(pgm, sha256(pgm), ngId);
    } finally {
      fixture.close();
    }

    const changed = await request({ "If-None-Match": etag });
    assert.equal(changed.status, 200);
    const updated = Buffer.from(await changed.arrayBuffer());
    assert.notDeepEqual(updated, original);
    assert.notEqual(changed.headers.get("etag"), etag);
    assert.equal(changed.headers.get("etag"), '"' + sha256(updated).toUpperCase() + '"');

    const document = await (await get("/openapi/v1.json")).json();
    const operation = document.paths["/api/inspections/{id}/image"].get;
    assert.ok(operation.parameters.some(p => p.in === "header" && p.name === "If-None-Match"));
    assert.equal(operation.responses["200"].content["image/bmp"].schema.format, "binary");
    assert.ok(!operation.responses["304"].content);
    assert.ok(operation.responses["412"].content["application/problem+json"]);
    for (const status of ["200", "304"]) {
      assert.ok(operation.responses[status].headers.ETag);
      assert.ok(operation.responses[status].headers["Cache-Control"]);
    }
    console.log(`Image cache passed: 200 (${original.byteLength} BMP bytes), 304 (0 bytes), changed 200, 404, 412 and OpenAPI.`);
  }, { Contract__Enabled: "true" });
}

if (process.argv.includes("--review")) {
  // Reproduce a lesson 03 database without the review table.
  const { DatabaseSync } = await import("node:sqlite");
  const legacy = new DatabaseSync(database);
  legacy.exec("DROP TABLE IF EXISTS Reviews");
  legacy.close();

  const users = ["alice", "bob", "viewer"];
  const passwords = Object.fromEntries(users.map(user => [user, "local-verification-" + user]));
  const environment = { Contract__Enabled: "true", Review__Enabled: "true",
    Review__Alice: passwords.alice, Review__Bob: passwords.bob, Review__Viewer: passwords.viewer };
  const reviewPath = api + "/" + ngId + "/review";

  function browser(origin) {
    const cookies = new Map();
    return async (path, body, csrf, method = body === undefined ? "GET" : "POST") => {
      const headers = { cookie: [...cookies.values()].join("; ") };
      if (csrf) headers["X-CSRF-Token"] = csrf;
      if (body !== undefined) headers["Content-Type"] = "application/json";
      const response = await fetch(origin + path, {
        method, headers, body: JSON.stringify(body), signal: AbortSignal.timeout(5000), redirect: "manual",
      });
      for (const cookie of response.headers.getSetCookie()) {
        const pair = cookie.split(";")[0];
        cookies.set(pair.split("=")[0], pair);
      }
      return response;
    };
  }

  async function signIn(client, user) {
    const guest = await (await client("/api/session")).json();
    const response = await client("/api/session/login", { user, password: passwords[user] }, guest.csrf);
    assert.equal(response.status, 204);
    const session = await (await client("/api/session")).json();
    assert.equal(session.user, user);
    return session.csrf;
  }

  const input = { decision: "confirmed-ng", note: "Verified against the original image" };
  let winner;
  await withServer(async (_get, origin) => {
    const document = await (await _get("/openapi/v1.json")).json();
    const security = document.components.securitySchemes.localSession;
    assert.equal(security.in, "cookie");
    assert.equal(security.name, "QualityTrace.Session");
    const submit = document.paths["/api/inspections/{id}/review"].post;
    assert.deepEqual(submit.security, [{ localSession: [] }]);
    assert.match(submit.description, /reviewer role/);
    for (const code of ["201", "400", "401", "403", "404", "409", "500"]) assert.ok(submit.responses[code]);
    for (const path of ["/api/session/login", "/api/session/logout", "/api/inspections/{id}/review"]) {
      const operation = document.paths[path].post;
      assert.ok(operation.parameters.some(parameter =>
        parameter.name === "X-CSRF-Token" && parameter.in === "header" && parameter.required));
    }
    assert.equal(document.paths["/api/session"].get.security?.length ?? 0, 0);
    assert.equal(document.paths["/api/session/login"].post.security?.length ?? 0, 0);
    assert.ok(document.paths["/api/session/login"].post.requestBody.content["application/json"]);
    const guest = browser(origin);
    const viewer = browser(origin);
    const alice = browser(origin);
    const bob = browser(origin);
    assert.equal((await guest(reviewPath, input)).status, 401);
    assert.equal((await guest(reviewPath)).status, 401);
    assert.equal((await guest("/api/session/login", { user: "alice", password: passwords.alice })).status, 400);
    const viewerToken = await signIn(viewer, "viewer");
    assert.equal((await viewer(reviewPath, input, viewerToken)).status, 403);
    assert.deepEqual(await (await viewer(reviewPath)).json(), { review: null });
    const aliceToken = await signIn(alice, "alice");
    const bobToken = await signIn(bob, "bob");
    assert.equal((await alice(reviewPath, input)).status, 400);
    assert.equal((await alice(reviewPath, input, bobToken)).status, 400);
    for (const invalid of [
      { decision: "invalid", note: " " },
      { ...input, note: "\u0085\u2000" },
      { ...input, note: "x".repeat(501) },
    ]) {
      const response = await alice(reviewPath, invalid, aliceToken);
      assert.equal(response.status, 400);
      const problem = await response.json();
      assert.ok(problem.errors.note.length);
      if (invalid.decision === "invalid") assert.ok(problem.errors.decision.length);
      assert.equal(problem.requestId, response.headers.get("x-request-id"));
      assert.deepEqual(await (await alice(reviewPath)).json(), { review: null });
    }
    const errorSchema = submit.responses["400"].content["application/problem+json"].schema;
    assert.equal(errorSchema.properties.errors.additionalProperties.type, "array");
    const okId = (await (await alice(api + "?result=OK")).json()).items[0].id;
    assert.equal((await alice(api + "/" + okId + "/review", input, aliceToken)).status, 409);

    const results = await Promise.all([
      alice(reviewPath, { ...input, reviewer: "forged" }, aliceToken),
      bob(reviewPath, { ...input, decision: "false-reject" }, bobToken),
    ]);
    assert.deepEqual(results.map(response => response.status).sort(), [201, 409]);
    const index = results.findIndex(response => response.status === 201);
    winner = await results[index].json();
    assert.equal(winner.reviewer, ["alice", "bob"][index]);
    assert.ok(winner.reviewedAt.endsWith("+00:00"));
    assert.deepEqual(await (await viewer(reviewPath)).json(), { review: winner });
    assert.equal((await alice("/api/session/logout", undefined, aliceToken, "POST")).status, 204);
    assert.equal((await alice(reviewPath)).status, 401);
  }, environment);

  await withServer(async (_get, origin) => {
    const client = browser(origin);
    await signIn(client, "viewer");
    assert.deepEqual(await (await client(reviewPath)).json(), { review: winner });
    assert.equal((await (await client(api + "/" + ngId)).json()).result, "NG");
  }, environment);
  console.log("Review schema upgrade, 401/403, CSRF, competing submissions, audit, logout and restart passed.");
}

if (process.argv.includes("--events")) {
  const { DatabaseSync } = await import("node:sqlite");
  const { consumeInspectionEvents } = await import("./consume-events.mjs");
  const localPath = join(directory, "consumer.db");
  const environment = { Contract__Enabled: "true", Events__Enabled: "true" };
  const events = "/api/inspection-events";
  let previous;
  await withServer(async (get, origin) => {
    assert.equal((await get(events + "?after=-1")).status, 400);
    assert.equal((await get(events + "?after=9223372036854775808")).status, 400);
    assert.equal((await get(events + "?limit=101")).status, 400);
    assert.equal((await get(events + "?after=100")).status, 409);
    const first = await (await get(events + "?limit=1")).json();
    assert.equal(first.items.length, 1);
    assert.equal(first.hasMore, true);
    const document = await (await get("/openapi/v1.json")).json();
    const operation = document.paths["/api/inspection-events"].get;
    assert.equal(operation.operationId, "ReadInspectionEvents");
    assert.equal(operation.security?.length ?? 0, 0);
    for (const code of ["200", "400", "403", "409", "500"]) assert.ok(operation.responses[code]);
    assert.ok(operation.parameters.some(parameter => parameter.name === "after" && parameter.schema.type === "string"));
    assert.ok(operation.parameters.some(parameter => parameter.name === "limit" && parameter.schema.type.includes("integer")));
    const pageSchema = document.components.schemas.InspectionEventPage;
    assert.deepEqual(Object.keys(first).sort(), Object.keys(pageSchema.properties).sort());
    assert.equal(document.components.schemas.InspectionEventItem.properties.sequence.type, "string");
    assert.equal(document.components.schemas.InspectionEventPage.properties.nextAfter.type, "string");
    previous = await consumeInspectionEvents(origin, localPath, 1);
    assert.equal(previous.processed, 2);
    const empty = await (await get(events + "?after=" + previous.after)).json();
    assert.deepEqual(empty.items, []);
    assert.equal(empty.nextAfter, previous.after);
    assert.equal(empty.hasMore, false);
  }, environment);

  // The API is offline while an independent importer writes another evidence batch.
  const nextDemo = execFileSync("dotnet", [evidenceApp, "--demo"], { encoding: "utf8" });
  const nextRoot = nextDemo.match(/retained: (.+); closed=True/i)?.[1];
  assert.ok(nextRoot, nextDemo);
  const newEvidence = join(nextRoot, "evidence");
  const newBundles = readdirSync(newEvidence, { withFileTypes: true })
    .filter(entry => entry.isDirectory()).map(entry => entry.name);
  assert.equal(newBundles.length, 2);
  for (const name of newBundles) assert.match(importBundle(name, "station-a", newEvidence), /Imported/);
  assert.match(importBundle(newBundles[0], "station-a", newEvidence), /Already imported/);

  await withServer(async (get, origin) => {
    const remaining = await (await get(events + "?after=" + previous.after)).json();
    assert.deepEqual(remaining.items.map(item => item.inspectionId), newBundles);
    const local = new DatabaseSync(localPath);
    try {
      local.exec(`
        CREATE TRIGGER FailSecondReceipt BEFORE INSERT ON Receipts
        WHEN NEW.Result = 'NG'
        BEGIN
          SELECT RAISE(ABORT, 'simulated consumer failure');
        END;
      `);
      await assert.rejects(consumeInspectionEvents(origin, localPath), /simulated consumer failure/);
      assert.equal(local.prepare("SELECT count(*) AS Count FROM Receipts").get().Count, 2);
      assert.equal(local.prepare("SELECT After FROM Checkpoint").get().After, previous.after);
      local.exec("DROP TRIGGER FailSecondReceipt");

      const resumed = await consumeInspectionEvents(origin, localPath);
      assert.equal(resumed.processed, 2);
      assert.equal(local.prepare("SELECT count(*) AS Count FROM Receipts").get().Count, 4);
      assert.deepEqual(await consumeInspectionEvents(origin, localPath), { processed: 0, after: resumed.after });
    } finally {
      local.close();
    }
  }, environment);

  const producer = new DatabaseSync(database);
  producer.exec("UPDATE InspectionEventStream SET StreamId = lower(hex(randomblob(16)))");
  producer.close();
  await withServer(async (_get, origin) => {
    await assert.rejects(consumeInspectionEvents(origin, localPath), /different event stream/);
  }, environment);

  console.log("Event backfill, ordered paging, offline import, rollback/resume, deduplication and stream identity passed.");
}

if (process.argv.includes("--mes")) {
  const { DatabaseSync } = await import("node:sqlite");
  const { consumeInspectionEvents } = await import("./consume-events.mjs");
  const { createMesReceiver } = await import("./mes-receiver.mjs");
  const { auditDelivery } = await import("./audit-delivery.mjs");
  const mesFile = join(directory, "mes.db");
  const localFile = join(directory, "http-consumer.db");

  async function openReceiver(options) {
    const server = createMesReceiver(mesFile, options);
    await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
    return { server, origin: "http://127.0.0.1:" + server.address().port };
  }
  async function closeReceiver(server) {
    await new Promise(resolve => { server.close(resolve); server.closeAllConnections(); });
  }

  let dropped = false;
  let mes = await openReceiver({ afterCommit: ({ response, created }) => {
    if (created && !dropped) {
      dropped = true;
      response.destroy();
      return true;
    }
    return false;
  } });
  let stableReceipt;
  try {
    await withServer(async (get, origin) => {
      await assert.rejects(consumeInspectionEvents(origin, localFile, 20, mes.origin));
      assert.equal(dropped, true);
      const interrupted = auditDelivery(database, localFile, mesFile);
      assert.deepEqual(interrupted.counts, { confirmed: 0, pending: 1, awaitingCheckpoint: 1 });
      assert.deepEqual(interrupted.issues, []);
      const source = new DatabaseSync(mesFile);
      stableReceipt = source.prepare("SELECT * FROM AcceptedResults").get();
      assert.equal(source.prepare("SELECT count(*) AS Count FROM AcceptedResults").get().Count, 1);
      source.close();

      const local = new DatabaseSync(localFile);
      assert.equal(local.prepare("SELECT count(*) AS Count FROM Checkpoint").get().Count, 0);
      local.exec(`
        CREATE TRIGGER FailReceipt BEFORE INSERT ON Receipts
        WHEN NEW.Result = 'NG'
        BEGIN SELECT RAISE(ABORT, 'fail local receipt after MES success'); END;
      `);

      const feed = await (await get("/api/inspection-events")).json();
      const item = feed.items[0];
      const body = { streamId: feed.streamId, sequence: item.sequence,
        inspectionId: item.inspectionId, result: item.result };
      const request = value => fetch(mes.origin + "/results", {
        method: "POST", headers: { "Content-Type": "application/json",
          "Idempotency-Key": body.streamId + ":" + body.sequence },
        body: JSON.stringify(value), signal: AbortSignal.timeout(5000),
      });
      const duplicates = await Promise.all([request(body), request(body)]);
      for (const response of duplicates) {
        assert.equal(response.status, 200);
        assert.deepEqual(await response.json(), { ...stableReceipt });
      }
      assert.equal((await request({ ...body, result: body.result === "OK" ? "NG" : "OK" })).status, 409);

      await assert.rejects(consumeInspectionEvents(origin, localFile, 20, mes.origin),
        /fail local receipt after MES success/);
      assert.equal(local.prepare("SELECT count(*) AS Count FROM Checkpoint").get().Count, 0);
      assert.equal(local.prepare("SELECT count(*) AS Count FROM Receipts").get().Count, 0);
      local.exec("DROP TRIGGER FailReceipt");
      local.close();
      const committed = new DatabaseSync(mesFile);
      assert.equal(committed.prepare("SELECT count(*) AS Count FROM AcceptedResults").get().Count, feed.items.length);
      committed.close();
    }, { Events__Enabled: "true" });
  } finally {
    await closeReceiver(mes.server);
  }

  // Restart the receiver on the same port, retaining its database and target identity.
  const port = new URL(mes.origin).port;
  const restarted = createMesReceiver(mesFile);
  await new Promise(resolve => restarted.listen(Number(port), "127.0.0.1", resolve));
  try {
    await withServer(async (_get, origin) => {
      const resumed = await consumeInspectionEvents(origin, localFile, 20, mes.origin);
      assert.ok(resumed.processed >= 2);
      assert.equal((await consumeInspectionEvents(origin, localFile, 20, mes.origin)).processed, 0);
      await assert.rejects(consumeInspectionEvents(origin, localFile), /delivery target/);
      const db = new DatabaseSync(mesFile);
      assert.deepEqual(db.prepare("SELECT * FROM AcceptedResults WHERE EventKey = ?").get(stableReceipt.EventKey), stableReceipt);
      assert.equal(db.prepare("SELECT count(*) AS Count FROM AcceptedResults").get().Count, resumed.processed);
      db.close();
    }, { Events__Enabled: "true" });
  } finally {
    await closeReceiver(restarted);
  }
  const healthy = auditDelivery(database, localFile, mesFile);
  assert.deepEqual(healthy.counts, { confirmed: 2, pending: 0, awaitingCheckpoint: 0 });
  assert.deepEqual(healthy.issues, []);
  const auditDb = new DatabaseSync(mesFile);
  auditDb.prepare("UPDATE AcceptedResults SET ReceiptId = ? WHERE EventKey = ?")
    .run("different-receipt", stableReceipt.EventKey);
  auditDb.close();
  const files = [database, localFile, mesFile];
  const before = files.map(path => readFileSync(path));
  const report = auditDelivery(...files);
  assert.deepEqual(report.issues.map(issue => issue.code), ["ReceiptConflict"]);
  assert.deepEqual(files.map(path => readFileSync(path)), before);
  const auditTool = fileURLToPath(new URL("./audit-delivery.mjs", import.meta.url));
  assert.throws(() => execFileSync(process.execPath, [auditTool, ...files], { stdio: "pipe" }),
    error => error.status === 3 && JSON.parse(error.stdout).issues[0].code === "ReceiptConflict");
  console.log("MES retry, conflict, rollback, restart and read-only delivery audit passed.");
}

if (process.argv.includes("--range")) {
  const { DatabaseSync } = await import("node:sqlite");
  const db = new DatabaseSync(database);
  const ticks = 621355968000000000n + BigInt(Date.parse("2026-11-05T01:00:00.000Z")) * 10000n;
  const ids = db.prepare("SELECT Id FROM Inspections ORDER BY Id").all();
  db.prepare("UPDATE Inspections SET ImportedTicks = ? WHERE Id = ?").run(ticks, ids[0].Id);
  db.prepare("UPDATE Inspections SET ImportedTicks = ? WHERE Id = ?").run(ticks + 36000000000n, ids[1].Id);
  db.close();

  await withServer(async get => {
    const path = (since, until) => api + "?" + new URLSearchParams({ since, until });
    const firstHour = await (await get(path("2026-11-05T01:00:00.000Z", "2026-11-05T02:00:00.000Z"))).json();
    assert.equal(firstHour.total, 1);
    assert.equal(firstHour.items[0].id, ids[0].Id);
    const secondHour = await (await get(path("2026-11-05T02:00:00.000Z", "2026-11-05T03:00:00.000Z"))).json();
    assert.equal(secondHour.items[0].id, ids[1].Id);
    assert.equal(secondHour.total, 1);
    assert.equal((await (await get(path("", "2026-11-05T01:00:00.000Z"))).json()).total, 0);
    assert.equal((await (await get(path("2026-11-05T02:00:00.000Z", ""))).json()).total, 1);
    for (const [since, until] of [
      ["2026-11-05T02:00:00.000Z", "2026-11-05T01:00:00.000Z"],
      ["2026-11-05T01:00:00.000Z", "2026-11-05T01:00:00.000Z"],
      ["2026-02-30T01:00:00.000Z", ""], ["2026-11-05T01:00:00", ""],
    ]) assert.equal((await get(path(since, until))).status, 400);
  });
  console.log("UTC range: inclusive start, exclusive end, open bounds and invalid input passed.");
}

if (process.argv.includes("--csv")) {
  await withServer(async get => {
    const response = await get(api + "?format=csv&page=2&pageSize=1");
    assert.equal(response.status, 200);
    assert.match(response.headers.get("content-disposition"), /attachment; filename=inspections.csv/);
    assert.match(response.headers.get("content-type"), /text\/csv/);
    const bytes = Buffer.from(await response.arrayBuffer());
    assert.deepEqual([...bytes.subarray(0, 3)], [239, 187, 191]);
    const text = bytes.subarray(3).toString("utf8");
    assert.equal(text.split("\r\n").length, 4);
    assert.match(text, /"OK"/);
    assert.match(text, /"NG"/);
    const ng = await (await get(api + "?format=csv&result=NG")).text();
    assert.match(ng, /"NG"/);
    assert.doesNotMatch(ng, /"OK"/);
    assert.equal((await (await get(api + "?format=csv&station=missing")).text()).trim(),
      "Id,Station,ImportedAtUtc,Result,RoiMean");
    assert.equal((await get(api + "?format=csv&since=invalid")).status, 400);
  });

  const { DatabaseSync } = await import("node:sqlite");
  const db = new DatabaseSync(database);
  const station = '=SUM(1,2)"中文';
  db.prepare("UPDATE Inspections SET Station = ? WHERE Id = ?").run(station, ngId);
  await withServer(async get => {
    const text = await (await get(api + "?" + new URLSearchParams({ format: "csv", station }))).text();
    assert.ok(text.includes('"\'=SUM(1,2)""中文"'));
  });
  db.exec(`
    WITH RECURSIVE numbers(n) AS (
      SELECT 1 UNION ALL SELECT n+1 FROM numbers WHERE n<1001
    )
    INSERT INTO Inspections
    SELECT 'csv-test-' || n, 'large', ImportedTicks, Result, RoiMean, Width, Height, ImageSha256, Pgm
    FROM numbers CROSS JOIN (SELECT * FROM Inspections LIMIT 1);
  `);
  db.close();
  await withServer(async get => {
    const response = await get(api + "?format=csv&station=large");
    assert.equal(response.status, 422);
    assert.doesNotMatch(response.headers.get("content-type"), /text\/csv/);
  });
  console.log("CSV filters, full result export, headers/BOM, quotes, formula prefix and row cap passed.");
}

if (process.argv.includes("--diagnostics")) {
  const { DatabaseSync } = await import("node:sqlite");
  await withServer(async (get, origin, logs) => {
    async function probe(kind, status, body) {
      const response = await get("/health/" + kind);
      assert.equal(response.status, status);
      assert.match(response.headers.get("cache-control"), /no-store/);
      assert.match(response.headers.get("x-request-id"), /^[0-9a-f]{32}$/);
      assert.equal(await response.text(), body);
    }
    await probe("live", 200, "Healthy");
    await probe("ready", 200, "Healthy");
    const ids = new Set();
    for (const [path, status] of [
      [api + "?page=0", 400], [api + "?page=bad", 400],
      [api + "/missing", 404], ["/api/no-such-route", 404],
    ]) {
      const response = await get(path);
      assert.equal(response.status, status);
      const problem = await response.json();
      const id = response.headers.get("x-request-id");
      assert.match(id, /^[0-9a-f]{32}$/);
      assert.equal(problem.requestId, id);
      ids.add(id);
    }
    assert.equal(ids.size, 4);
    const healthy = await fetch(origin + api, { headers: { "X-Request-Id": "client-supplied" } });
    assert.match(healthy.headers.get("x-request-id"), /^[0-9a-f]{32}$/);

    const broken = new DatabaseSync(database);
    broken.exec("ALTER TABLE Inspections RENAME TO DiagnosticBrokenInspections");
    try {
      await probe("live", 200, "Healthy");
      await probe("ready", 503, "Unhealthy");
      const response = await get(api);
      assert.equal(response.status, 500);
      const body = await response.text();
      const id = response.headers.get("x-request-id");
      assert.equal(JSON.parse(body).requestId, id);
      assert.doesNotMatch(body, /SqliteException|no such table|DiagnosticBrokenInspections/);
      for (let i = 0; i < 20 && !logs().includes("Request " + id); i++) {
        await new Promise(resolve => setTimeout(resolve, 50));
      }
      assert.ok(logs().includes("Request " + id), logs());
      assert.ok(logs().includes("returned 500"));
    } finally {
      broken.exec("ALTER TABLE DiagnosticBrokenInspections RENAME TO Inspections");
      broken.close();
    }
    assert.equal((await get(api)).status, 200);
    await probe("ready", 200, "Healthy");
  }, { ASPNETCORE_ENVIRONMENT: "Production" });
  console.log("Diagnostics: request IDs, errors, liveness and readiness during SQLite failure/recovery passed.");
}

if (process.argv.includes("--contract")) {
  await withServer(async get => {
    const response = await get("/openapi/v1.json");
    assert.equal(response.status, 200);
    const document = await response.json();
    const list = document.paths["/api/inspections"].get;
    assert.equal(list.operationId, "ListInspections");
    assert.deepEqual(Object.keys(document.paths).sort(),
      ["/api/inspection-pages", "/api/inspections", "/api/inspections/{id}", "/api/inspections/{id}/evidence", "/api/inspections/{id}/image"]);
    for (const name of ["station", "result", "page", "pageSize", "since", "until", "format", "newest"]) {
      assert.ok(list.parameters.some(parameter => parameter.name === name && parameter.in === "query"));
    }
    assert.ok(list.responses["200"].content["application/json"]);
    assert.ok(list.responses["200"].content["text/csv"]);
    assert.ok(list.responses["422"]);
    assert.ok(list.responses["500"]);
    const page = document.components.schemas.InspectionPage;
    for (const field of ["items", "total", "page", "pageSize"]) assert.ok(page.properties[field]);
    const actual = await (await get(api + "?page=1&pageSize=1")).json();
    assert.deepEqual(Object.keys(actual).sort(), Object.keys(page.properties).sort());
    const csv = await get(api + "?format=csv");
    assert.match(csv.headers.get("content-type"), /text\/csv/);
    assert.equal((await get(api + "?page=0")).status, 400);
    assert.equal((await get(api + "/missing")).status, 404);
  }, { Contract__Enabled: "true" });
  await withServer(async get => {
    assert.equal((await get("/openapi/v1.json")).status, 404);
  }, { Contract__Enabled: "false" });
  console.log("OpenAPI query parameters, response schemas, CSV, errors and opt-in route passed.");
}

if (process.argv.includes("--backup")) {
  const { DatabaseSync } = await import("node:sqlite");
  const snapshot = join(directory, "snapshot.db");
  const restored = join(directory, "restored.db");
  const environment = {
    Events__Enabled: "true", Review__Enabled: "true",
    Review__Alice: "backup-test-alice", Review__Bob: "backup-test-bob", Review__Viewer: "backup-test-viewer",
  };
  const backup = (source, destination) => execFileSync("dotnet",
    [app, "--TraceDb", source, "--Backup", destination], { encoding: "utf8", stdio: "pipe" });

  const producer = new DatabaseSync(database);
  let originalEvents;
  try {
    producer.exec("PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;");
    await withServer(async get => {
      originalEvents = await (await get("/api/inspection-events?limit=1")).json();
      producer.prepare("INSERT INTO Reviews VALUES (?, ?, ?, ?, ?)").run(ngId, "confirmed-ng",
        "before snapshot", "alice", "2026-11-06T01:00:00+00:00");
      assert.ok(readFileSync(database + "-wal").length > 32);
      assert.match(backup(database, snapshot), /Backup created/);
      producer.prepare("UPDATE Reviews SET Note = ? WHERE InspectionId = ?").run("after snapshot", ngId);
    }, environment);
  } finally {
    producer.close();
  }

  const bytes = readFileSync(snapshot);
  assert.throws(() => backup(database, snapshot), /destination already exists/);
  assert.deepEqual(readFileSync(snapshot), bytes);
  const missing = join(directory, "missing.db");
  assert.throws(() => backup(missing, join(directory, "failed-backup.db")));
  assert.ok(!readdirSync(directory).includes("failed-backup.db"));
  assert.ok(!readdirSync(directory).some(name => name.startsWith("snapshot.db-") || name.endsWith(".pending")));

  assert.match(backup(snapshot, restored), /Backup created/);
  const copy = new DatabaseSync(restored, { readOnly: true });
  try {
    const saved = copy.prepare("SELECT Note, Reviewer FROM Reviews WHERE InspectionId = ?").get(ngId);
    assert.equal(saved.Note, "before snapshot");
    assert.equal(saved.Reviewer, "alice");
  } finally {
    copy.close();
  }

  await withServer(async get => {
    const detail = await (await get(api + "/" + ngId)).json();
    assert.equal(detail.result, "NG");
    assert.equal(detail.roiMean, 75);
    const image = await get(api + "/" + ngId + "/image");
    assert.equal(image.headers.get("content-type"), "image/bmp");
    const events = await (await get("/api/inspection-events?limit=1")).json();
    assert.deepEqual(events, originalEvents);
    const next = await (await get("/api/inspection-events?after=" + events.nextAfter)).json();
    assert.equal(next.items.length, 1);
    assert.equal(next.hasMore, false);
  }, environment, restored);
  console.log("Backup of an active WAL database, collision refusal, isolated restore, review, image and cursor passed.");
}

if (process.argv.includes("--cursor-pages")) {
  const { DatabaseSync } = await import("node:sqlite");
  execFileSync("dotnet", [app, "--TraceDb", database, "--Migrate", "true"], { stdio: "pipe" });
  const fixture = new DatabaseSync(database);
  const since = "2025-01-01T00:00:00.000Z";
  const until = "2025-01-02T00:00:00.000Z";
  const toTicks = value => 621355968000000000n + BigInt(Date.parse(value)) * 10000n;
  const lower = toTicks(since);
  const upper = toTicks(until);
  const insert = fixture.prepare(`
    INSERT INTO Inspections (Id, Station, ImportedTicks, Result, RoiMean, Width, Height, ImageSha256, Pgm)
    VALUES (?, ?, ?, ?, 75, 1, 1, '', X'00')
  `);
  const station = "cursor-station";
  try {
    insert.run("cursor-z", station, lower + 2n, "NG");
    insert.run("cursor-b", station, lower + 1n, "NG");
    insert.run("cursor-a", station, lower + 1n, "NG");
    insert.run("cursor-old", station, lower, "NG");
    insert.run("before-range", station, lower - 1n, "NG");
    insert.run("at-until", station, upper, "NG");
    insert.run("wrong-result", station, lower + 3n, "OK");
    insert.run("wrong-station", "station-b", lower + 3n, "NG");

    await withServer(async get => {
      const path = "/api/inspection-pages";
      const query = new URLSearchParams({ station, result: "NG", pageSize: "2", since, until });
      const readPage = async params => {
        const response = await get(path + "?" + params);
        assert.equal(response.status, 200, await response.clone().text());
        return response.json();
      };
      const ids = page => page.items.map(row => row.id);
      const first = await readPage(query);
      assert.deepEqual(ids(first), ["cursor-z", "cursor-b"]);
      assert.equal(first.items[1].importedAt, "2025-01-01T00:00:00.0000001Z");
      assert.match(first.nextCursor, /^[A-Za-z0-9_-]+$/);
      insert.run("cursor-new", station, lower + 3n, "NG");
      const resumed = new URLSearchParams(query);
      resumed.set("after", first.nextCursor);
      const second = await readPage(resumed);
      assert.deepEqual(ids(second), ["cursor-a", "cursor-old"]);
      assert.equal(second.nextCursor, null);
      assert.deepEqual(ids(await readPage(query)), ["cursor-new", "cursor-z"]);

      for (const [key, value] of [
        ["station", "station-b"], ["result", "OK"], ["since", "2024-12-31T00:00:00.000Z"],
        ["until", "2025-01-03T00:00:00.000Z"], ["after", "%"],
        ["after", Buffer.from('{"Version":2}').toString("base64url")], ["after", "x".repeat(2049)],
        ["pageSize", "101"],
      ]) {
        const invalid = new URLSearchParams(resumed);
        invalid.set(key, value);
        const response = await get(path + "?" + invalid);
        assert.equal(response.status, 400, key);
        assert.ok((await response.json()).requestId, key);
      }
      assert.equal((await get(path)).status, 400);
      query.set("station", "empty-station");
      assert.deepEqual(await readPage(query), { items: [], nextCursor: null });

      const document = await (await get("/openapi/v1.json")).json();
      const operation = document.paths[path].get;
      assert.equal(operation.operationId, "ListInspectionCursorPage");
      assert.deepEqual(operation.parameters.filter(p => p.required).map(p => p.name), ["station", "result"]);
      assert.ok(operation.responses["400"]);
      assert.deepEqual(Object.keys(document.components.schemas.InspectionCursorPage.properties).sort(),
        Object.keys(first).sort());
    }, { Contract__Enabled: "true" });
  } finally {
    fixture.close();
  }
  console.log("Cursor HTTP paging, exact ticks, UTC bounds, head insert, final page, invalid scope and OpenAPI passed.");
}

if (process.argv.includes("--migrate")) {
  const { DatabaseSync } = await import("node:sqlite");
  const { execFile } = await import("node:child_process");
  const { promisify } = await import("node:util");
  const index = "IX_Inspections_Station_Result_ImportedTicks_Id";
  const upgraded = join(directory, "upgrade-copy.db");
  const conflict = join(directory, "conflicting-copy.db");
  for (const target of [upgraded, conflict]) {
    execFileSync("dotnet", [app, "--TraceDb", database, "--Backup", target], { stdio: "pipe" });
  }
  const migrateArgs = file => [app, "--TraceDb", file, "--Migrate", "true"];
  const upgrade = await Promise.all([1, 2].map(() =>
    promisify(execFile)("dotnet", migrateArgs(upgraded), { encoding: "utf8" })));
  assert.equal(upgrade.filter(result => result.stdout.includes("upgraded to version 1")).length, 1);
  assert.equal(upgrade.filter(result => result.stdout.includes("already at version 1")).length, 1);

  const original = new DatabaseSync(database, { readOnly: true });
  const migrated = new DatabaseSync(upgraded, { readOnly: true });
  const readRows = db => db.prepare("SELECT Id, Station, Result, RoiMean FROM Inspections ORDER BY Id").all();
  try {
    assert.equal(original.prepare("PRAGMA user_version").get().user_version, 0);
    assert.equal(migrated.prepare("PRAGMA user_version").get().user_version, 1);
    const columns = migrated.prepare("SELECT name FROM pragma_index_info(?) ORDER BY seqno").all(index);
    assert.deepEqual(columns.map(row => row.name), ["Station", "Result", "ImportedTicks", "Id"]);
    assert.deepEqual(readRows(migrated), readRows(original));
  } finally {
    original.close();
    migrated.close();
  }

  const invalid = new DatabaseSync(conflict);
  try {
    invalid.exec("CREATE INDEX " + index + " ON Inspections (Station)");
    assert.throws(() => execFileSync("dotnet", migrateArgs(conflict), { stdio: "pipe" }), /already exists/);
    assert.equal(invalid.prepare("PRAGMA user_version").get().user_version, 0);
    assert.deepEqual(invalid.prepare("SELECT name FROM pragma_index_info(?)").all(index).map(row => row.name), ["Station"]);
    invalid.exec("PRAGMA user_version=99");
    assert.throws(() => execFileSync("dotnet", migrateArgs(conflict), { stdio: "pipe" }), /Unsupported trace database version/);
    assert.equal(invalid.prepare("PRAGMA user_version").get().user_version, 99);
  } finally {
    invalid.close();
  }

  await withServer(async get => {
    assert.equal((await get("/health/ready")).status, 200);
    const page = await (await get(api + "?result=NG&station=station-a")).json();
    assert.equal(page.total, 1);
    assert.equal(page.items[0].id, ngId);
  }, {}, upgraded);
  console.log("Versioned migration, concurrent repeat, conflict rollback, unsupported version and API query passed.");
}
