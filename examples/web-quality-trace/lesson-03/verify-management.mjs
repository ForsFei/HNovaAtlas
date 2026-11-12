import assert from "node:assert/strict";
import { execFileSync, spawn } from "node:child_process";
import { once } from "node:events";
import { fileURLToPath } from "node:url";
import { randomUUID } from "node:crypto";
import { DatabaseSync } from "node:sqlite";

const here = fileURLToPath(new URL("./", import.meta.url));
const app = fileURLToPath(new URL("./bin/Debug/net10.0/QualityTrace.Storage.dll", import.meta.url));
const base = execFileSync(process.execPath, ["verify.mjs"], { cwd: here, encoding: "utf8" });
const database = base.match(/Database: (.+)/)?.[1].trim();
assert.ok(database, base);
const password = randomUUID();
const env = { ...process.env, Contract__Enabled: "true", Management__Enabled: "true", Management__User: "admin", Management__Password: password };
const command = flag => execFileSync("dotnet", [app, "--TraceDb", database, flag, "true"], { env, encoding: "utf8", stdio: "pipe" });
assert.match(command("--Management:Migrate"), /version 1/);
assert.match(command("--Management:Migrate"), /version 1/);
assert.match(command("--Management:Bootstrap"), /administrator created/);
assert.throws(() => command("--Management:Bootstrap"));
const times = new DatabaseSync(database);
try {
  const tick = 621355968000000000n + BigInt(Date.parse("2026-11-12T00:00:00Z")) * 10000n;
  times.prepare("UPDATE Inspections SET ImportedTicks = ? WHERE Result = 'NG'").run(tick);
  times.prepare("UPDATE Inspections SET ImportedTicks = ? WHERE Result = 'OK'").run(tick + 864000000000n);
} finally { times.close(); }

let server, exited, origin;
async function start() {
  server = spawn("dotnet", [app, "--TraceDb", database, "--urls", "http://127.0.0.1:0"], { env, windowsHide: true });
  exited = once(server, "exit");
  let log = "";
  server.stdout.on("data", chunk => { log += chunk; origin = log.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/)?.[1]; });
  server.stderr.on("data", chunk => { log += chunk; });
  for (let n = 0; !origin && n < 150 && server.exitCode === null; n++) await new Promise(r => setTimeout(r, 100));
  assert.ok(origin, log);
}
async function stop() { if (server.exitCode === null) server.kill(); await exited; origin = undefined; }
function client() {
  const cookies = new Map(); let csrf;
  async function request(path, method = "GET", body, token = csrf) {
    const response = await fetch(origin + path, { method, headers: { Cookie: [...cookies].map(([k,v]) => k+"="+v).join("; "),
      ...(body ? { "Content-Type": "application/json" } : {}), ...(token ? { "X-CSRF-Token": token } : {}) },
    body: body ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(10000) });
    for (const raw of response.headers.getSetCookie()) { const pair=raw.split(";",1)[0]; const at=pair.indexOf("="); cookies.set(pair.slice(0,at),pair.slice(at+1)); }
    return response;
  }
  async function session() { const r=await request("/api/session"); assert.equal(r.status,200); const s=await r.json(); csrf=s.csrf; return s; }
  async function login(user, secret=password) { await session(); const r=await request("/api/session/login","POST",{user,password:secret}); assert.equal(r.status,204); return session(); }
  return { request, login, session };
}
try {
  await start();
  const admin=client(), viewer=client(), reviewer=client(), anonymous=client();
  assert.equal((await anonymous.request("/api/management/stations")).status,401);
  assert.equal((await anonymous.request("/api/inspections")).status,401);
  assert.equal((await admin.login("admin")).canReview,true);
  assert.equal((await admin.request("/health/ready")).status,200);
  const contract=await (await admin.request("/openapi/v1.json")).json();
  assert.equal(contract.paths["/api/management/stations"].post.operationId,"CreateStationProfile");
  assert.ok(contract.paths["/api/management/accounts/{user}"].put.parameters.some(p=>p.name==="X-CSRF-Token"));
  assert.ok(contract.paths["/api/management/statistics"].get.security.length);
  const station={code:"station-c",name:"装配工位",enabled:true,version:1};
  assert.equal((await admin.request("/api/management/stations","POST",station,"wrong")).status,400);
  assert.equal((await admin.request("/api/management/stations","POST",{...station,code:"INVALID"})).status,400);
  assert.equal((await admin.request("/api/management/stations","POST",station)).status,201);
  assert.equal((await admin.request("/api/management/stations","POST",station)).status,409);
  const changed=await admin.request("/api/management/stations/station-c","PUT",{...station,name:"工位已修改",enabled:false});
  assert.equal(changed.status,200); assert.equal((await changed.json()).version,2);
  assert.equal((await admin.request("/api/management/stations/station-c","PUT",station)).status,409);
  for(const role of ["viewer","reviewer"]) {
    const r=await admin.request("/api/management/accounts","POST",{userName:role,role,password});
    assert.equal(r.status,201); assert.equal((await r.text()).includes("passwordHash"),false);
  }
  await viewer.login("viewer"); await reviewer.login("reviewer");
  assert.equal((await viewer.request("/api/management/stations")).status,200);
  assert.equal((await viewer.request("/api/management/stations","POST",station)).status,403);
  assert.equal((await reviewer.request("/api/management/accounts")).status,403);
  let stats=await (await viewer.request("/api/management/statistics")).json();
  assert.equal(stats.total,2); assert.equal(stats.ng,1); assert.equal(stats.pending,1); assert.equal(stats.ngRate,0.5);
  const range=await (await viewer.request("/api/management/statistics?since=2026-11-12T00:00:00.000Z&until=2026-11-13T00:00:00.000Z")).json();
  assert.equal(range.total,1);assert.equal(range.ng,1);assert.equal(range.pending,1);
  assert.equal((await viewer.request("/api/management/statistics?since=invalid")).status,400);
  const ng=(await (await reviewer.request("/api/inspections?result=NG")).json()).items[0];
  assert.equal((await reviewer.request(`/api/inspections/${ng.id}/review`,"POST",{decision:"confirmed-ng",note:"Persistent reviewer acceptance"})).status,201);
  stats=await (await viewer.request("/api/management/statistics")).json(); assert.equal(stats.pending,0);
  const empty=await (await viewer.request("/api/management/statistics?station=missing")).json(); assert.equal(empty.total,0);assert.equal(empty.ngRate,null);
  assert.equal((await admin.request("/api/management/accounts/viewer","PUT",{userName:"viewer",role:"viewer",enabled:false,version:1})).status,200);
  assert.equal((await viewer.request("/api/management/stations")).status,401);
  assert.equal((await admin.request("/api/management/accounts/admin","PUT",{userName:"admin",role:"viewer",enabled:true,version:1})).status,409);
  assert.equal((await admin.request("/api/management/accounts/reviewer","PUT",{userName:"reviewer",role:"viewer",enabled:true,version:1})).status,200);
  assert.equal((await reviewer.request("/api/management/stations")).status,401);
  assert.equal((await reviewer.login("reviewer")).canReview,false);
  const audit=await (await admin.request("/api/management/audit")).json(); assert.ok(audit.length>=6);
  const db=new DatabaseSync(database);try { const hashes=db.prepare("SELECT PasswordHash FROM TraceAccounts").all();assert.ok(hashes.every(r=>r.PasswordHash!==password && r.PasswordHash.length>40)); } finally { db.close(); }
  await stop();await start();
  const reopened=client(); await reopened.login("admin");
  const stations=await (await reopened.request("/api/management/stations")).json();assert.equal(stations[0].enabled,false);
  console.log("PASS management: explicit migration, bootstrap, CSRF, station validation/duplicate/concurrency/disable, role enforcement, persistent hashed users, session revocation, audit, statistics and restart.");
  console.log("Management fixture:",database);
} finally { if(server) await stop(); }
