import assert from "node:assert/strict";
import { execFileSync, spawn, spawnSync } from "node:child_process";
import { once } from "node:events";
import { mkdtempSync, readFileSync, copyFileSync, mkdirSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { randomUUID } from "node:crypto";
import http from "node:http";
import https from "node:https";
import { DatabaseSync } from "node:sqlite";

const root=fileURLToPath(new URL("../../../",import.meta.url));
const work=mkdtempSync(join(tmpdir(),"trace-deployment-"));
const release=join(work,"release-v1"),data=join(work,"data"),database=join(data,"trace.db");
const publish=fileURLToPath(new URL("./Publish-Trace.ps1",import.meta.url));
execFileSync("pwsh",["-NoProfile","-File",publish,"-Destination",release,"-DataDirectory",data],{cwd:root,stdio:"pipe",timeout:180000});
const runner=join(release,"Run-Trace.ps1"),app=join(release,"QualityTrace.Storage.dll");
const password=randomUUID();
const env={...process.env,Management__User:"admin",Management__Password:password};
const run=(mode,extra=[])=>execFileSync("pwsh",["-NoProfile","-File",runner,"-Mode",mode,...extra],{cwd:work,env,stdio:"pipe"});
run("Migrate");run("Bootstrap");
const generator=join(root,"examples/industrial-station-evidence/bin/Release/net10.0/IndustrialStation.Evidence.dll");
const demo=execFileSync("dotnet",[generator,"--demo"],{encoding:"utf8",cwd:work});
const evidence=join(demo.match(/retained: (.+); closed=True/i)[1],"evidence");
const {readdirSync}=await import("node:fs");
for(const entry of readdirSync(evidence,{withFileTypes:true}).filter(e=>e.isDirectory())) {
  execFileSync("dotnet",[app,"--contentRoot",release,"--Import",join(evidence,entry.name)],{cwd:work,env,stdio:"pipe"});
}
// Test-only certificate: no certificate store changes and no global TLS bypass.
const cert=join(work,"loopback.pem"),key=join(work,"loopback.key");
const ps=`$rsa=[Security.Cryptography.RSA]::Create(2048)
$request=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=127.0.0.1',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
$san=[Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
$san.AddIpAddress([Net.IPAddress]::Loopback)
$request.CertificateExtensions.Add($san.Build())
$cert=$request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-1),[DateTimeOffset]::UtcNow.AddHours(2))
[IO.File]::WriteAllText('${cert.replaceAll("'","''")}',$cert.ExportCertificatePem())
[IO.File]::WriteAllText('${key.replaceAll("'","''")}',$rsa.ExportPkcs8PrivateKeyPem())
$cert.Dispose();$rsa.Dispose()`;
execFileSync("pwsh",["-NoProfile","-Command",ps],{stdio:"pipe"});
let server,exited,origin,proxy,serverLog="";
async function start(db=database) {
  origin=undefined;let log="";serverLog="";
  server=spawn("dotnet",[app,"--contentRoot",release,"--TraceDb",db,"--urls","http://127.0.0.1:0"],{cwd:work,env,windowsHide:true});exited=once(server,"exit");
  server.stdout.on("data",c=>{log+=c;serverLog+=c;origin=log.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/)?.[1];});server.stderr.on("data",c=>{log+=c;serverLog+=c;});
  for(let n=0;!origin&&n<150&&server.exitCode===null;n++)await new Promise(r=>setTimeout(r,100));assert.ok(origin,log);
}
async function stop(){if(server?.exitCode===null)server.kill();if(exited)await exited;server=undefined;}
const jar=new Map();let csrf,tlsOrigin;
function request(path,method="GET",body) {
  return new Promise((resolve,reject)=>{
    const payload=body?JSON.stringify(body):undefined;
    const req=https.request(new URL(path,tlsOrigin),{method,ca:readFileSync(cert),headers:{Cookie:[...jar].map(([k,v])=>k+"="+v).join("; "),
      ...(payload?{"Content-Type":"application/json","Content-Length":Buffer.byteLength(payload)}:{}),...(csrf?{"X-CSRF-Token":csrf}:{})}},response=>{
      const chunks=[];response.on("data",c=>chunks.push(c));response.on("end",()=>{
        for(const raw of response.headers["set-cookie"]??[]){const p=raw.split(";",1)[0],at=p.indexOf("=");jar.set(p.slice(0,at),p.slice(at+1));}
        const text=Buffer.concat(chunks).toString();resolve({status:response.statusCode,headers:response.headers,text,json:()=>JSON.parse(text)});
      });
    });req.on("error",reject);req.setTimeout(10000,()=>req.destroy(new Error("TLS request timeout")));req.end(payload);
  });
}
try {
  await start();
  proxy=https.createServer({cert:readFileSync(cert),key:readFileSync(key)},(req,res)=>{
    const upstream=http.request(new URL(req.url,origin),{method:req.method,headers:{...req.headers,host:new URL(origin).host,"x-forwarded-proto":"https"}},reply=>{res.writeHead(reply.statusCode,reply.headers);reply.pipe(res);});
    upstream.on("error",()=>{res.writeHead(502);res.end();});req.pipe(upstream);
  });proxy.listen(0,"127.0.0.1");await once(proxy,"listening");tlsOrigin="https://127.0.0.1:"+proxy.address().port;
  assert.equal((await request("/health/ready")).status,200);
  assert.match((await request("/")).text,/<div id="root">/);
  assert.equal((await request("/api/inspections")).status,401);
  csrf=(await request("/api/session")).json().csrf;
  const login=await request("/api/session/login","POST",{user:"admin",password});assert.equal(login.status,204);
  assert.ok(login.headers["set-cookie"].some(c=>c.includes("QualityTrace.Session")&&/; secure/i.test(c)));
  csrf=(await request("/api/session")).json().csrf;
  assert.equal((await request("/api/management/statistics")).json().total,2);
  const backup=join(work,"snapshot.db");run("Backup",["-BackupPath",backup]);
  assert.throws(()=>run("Backup",["-BackupPath",backup]));
  assert.equal((await request("/api/management/stations","POST",{code:"after-backup",name:"快照后新增"})).status,201);
  await stop();
  const restored=join(work,"restore","trace.db");mkdirSync(join(work,"restore"));copyFileSync(backup,restored);
  execFileSync("dotnet",[app,"--TraceDb",restored,"--Migrate","true"],{cwd:work,env,stdio:"pipe"});
  const check=new DatabaseSync(restored);try {assert.equal(check.prepare("PRAGMA user_version").get().user_version,1);check.exec("UPDATE ManagementSchema SET Version=99");}finally{check.close();}
  const refused=spawnSync("dotnet",[app,"--contentRoot",release,"--TraceDb",restored,"--urls","http://127.0.0.1:0"],{cwd:work,env,encoding:"utf8",timeout:20000,windowsHide:true});
  assert.notEqual(refused.status,0);assert.match(refused.stdout+refused.stderr,/Unsupported management schema/);
  // Keep the failed upgrade copy. Roll back to another restored file, never overwrite it in place.
  const rollback=join(work,"rollback","trace.db");mkdirSync(join(work,"rollback"));copyFileSync(backup,rollback);
  await start(rollback);
  assert.equal((await request("/health/ready")).status,200);
  assert.equal((await request("/api/management/me")).json().user,"admin"); // same saved DP keys + cookie
  assert.deepEqual((await request("/api/management/stations")).json(),[]);
  const snapshot=new DatabaseSync(backup);try {assert.equal(snapshot.prepare("PRAGMA user_version").get().user_version,0);}finally{snapshot.close();}
  assert.equal((await request("/api/management/statistics")).json().total,2);
  execFileSync("dotnet",[app,"--contentRoot",release,"--TraceDb",rollback,"--Management:RevokeSessions","true"],{cwd:work,env,stdio:"pipe"});
  assert.equal((await request("/api/management/me")).status,401);
  csrf=(await request("/api/session")).json().csrf;
  assert.equal((await request("/api/session/login","POST",{user:"admin",password})).status,204);
  console.log("PASS deployment: independent release, manifest, explicit bootstrap, TLS proxy with trusted test certificate, secure cookie, live backup collision refusal, index migration, unsupported upgrade rejection and rollback snapshot with preserved session keys.");
  console.log("Retained deployment drill:",work);
}catch(error){console.error(serverLog.slice(-7000));throw error;}finally{await stop();if(proxy)await new Promise(r=>proxy.close(r));}
