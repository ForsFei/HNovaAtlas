import { useEffect, useRef, useState, type FormEvent } from "react";
import { z } from "zod";
import { TimeRangeForm } from "./TimeRangeForm";
import { readJson } from "./read-json";

const stationSchema = z.object({ code:z.string(), name:z.string(), enabled:z.boolean(), version:z.number().int() });
const accountSchema = z.object({ userName:z.string(), role:z.enum(["admin","reviewer","viewer"]), enabled:z.boolean(), version:z.number().int() });
const meSchema = z.object({user:z.string(),role:z.enum(["admin","reviewer","viewer"])});
const sessionSchema = z.object({user:z.string().nullable(),csrf:z.string()});
const statisticsSchema = z.object({ total:z.number(), ng:z.number(), pending:z.number(), ngRate:z.number().nullable(),
  stations:z.array(z.object({station:z.string(),total:z.number(),ng:z.number(),pending:z.number()})),timeBasis:z.string() });
type Station = z.infer<typeof stationSchema>;
type Account = z.infer<typeof accountSchema>;
const emptyStation:Station = {code:"",name:"",enabled:true,version:1};
const emptyAccount:Account = {userName:"",role:"viewer",enabled:true,version:1};

export function ManagementConsole({onTrace}:{onTrace:(filters?:Record<string,string>)=>void}) {
  const [me,setMe] = useState<z.infer<typeof meSchema>|null>(null);
  const [stations,setStations] = useState<Station[]>([]);
  const [stationCodes,setStationCodes] = useState<string[]>([]);
  const [accounts,setAccounts] = useState<Account[]>([]);
  const [stats,setStats] = useState<z.infer<typeof statisticsSchema>|null>(null);
  const [draft,setDraft] = useState(emptyStation);
  const [userDraft,setUserDraft] = useState(emptyAccount);
  const [editing,setEditing] = useState(false);
  const [editingUser,setEditingUser] = useState(false);
  const [password,setPassword] = useState("");
  const [error,setError] = useState("");
  const [busy,setBusy] = useState(true);
  const actionPending=useRef(false);
  const [query,setQuery] = useState({station:"",since:"",until:""});
  const token=useRef("");

  async function get<T>(path:string,schema:z.ZodType<T>,signal:AbortSignal=AbortSignal.timeout(15000)) {
    return readJson(path,schema,signal);
  }
  async function write(path:string,method:string,body?:unknown) {
    const r=await fetch(path,{method,headers:{"Content-Type":"application/json","X-CSRF-Token":token.current},
      body:body ? JSON.stringify(body):undefined,signal:AbortSignal.timeout(15000)});
    if(!r.ok) {
      if(r.status===401) {setMe(null);setAccounts([]);setStats(null);}
      const text=await r.text();
      let message="操作失败";
      try { const p=JSON.parse(text);message=p.title ?? message;if(p.errors) message+="："+Object.values(p.errors).flat().join("；"); } catch { /* HTTP status remains useful. */ }
      throw new Error(`${message}（HTTP ${r.status}，请求 ${r.headers.get("x-request-id") ?? "未知"}）。写入结果不确定时请先刷新核对。`);
    }
    return r;
  }
  async function loadStats(filters=query,signal?:AbortSignal) {
    setStats(null);
    const p=new URLSearchParams();
    for(const [key,value] of Object.entries(filters)) if(value) p.set(key,value);
    const data=await get("/api/management/statistics?"+p,statisticsSchema,signal);
    if(!signal?.aborted) setStats(data);
  }
  async function refresh(signal?:AbortSignal) {
    const session=await get("/api/session",sessionSchema,signal);
    if(signal?.aborted)return;token.current=session.csrf;
    if(!session.user) {setMe(null);setStations([]);setAccounts([]);setStats(null);return;}
    const identity=await get("/api/management/me",meSchema,signal);
    if(signal?.aborted)return;setMe(identity);
    const configured=await get("/api/management/stations",z.array(stationSchema),signal);
    if(signal?.aborted)return;setStations(configured);
    const codes=await get("/api/management/station-codes",z.array(z.string()),signal);
    if(signal?.aborted)return;setStationCodes(codes);
    if(identity.role==="admin") {
      const users=await get("/api/management/accounts",z.array(accountSchema),signal);
      if(signal?.aborted)return;setAccounts(users);
    }else setAccounts([]);
    await loadStats(query,signal);
  }
  async function run(action:()=>Promise<void>) {
    if(actionPending.current) return;
    actionPending.current=true;setBusy(true);setError("");
    try {await action();} catch(e) {setError(e instanceof Error?e.message:"操作失败");} finally {actionPending.current=false;setBusy(false);}
  }
  useEffect(()=>{
    const controller=new AbortController();
    void Promise.resolve().then(()=>refresh(controller.signal)).catch(e=>{
      if(!controller.signal.aborted)setError(e instanceof Error?e.message:"读取失败");
    }).finally(()=>{if(!controller.signal.aborted)setBusy(false);});
    return ()=>controller.abort();
    // Initial session load uses the initial empty statistics range. Later actions use current state.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  },[]);

  function login(event:FormEvent<HTMLFormElement>) {
    event.preventDefault();const form=event.currentTarget;const data=new FormData(form);
    void run(async()=>{const session=await get("/api/session",sessionSchema);token.current=session.csrf;
      await write("/api/session/login","POST",{user:data.get("user"),password:data.get("password")});
      form.reset();await refresh();});
  }
  function saveStation(event:FormEvent) {
    event.preventDefault();
    void run(async()=>{await write(editing?"/api/management/stations/"+encodeURIComponent(draft.code):"/api/management/stations",editing?"PUT":"POST",draft);
      setDraft(emptyStation);setEditing(false);await refresh();});
  }
  function saveUser(event:FormEvent) {
    event.preventDefault();
    void run(async()=>{await write(editingUser?"/api/management/accounts/"+encodeURIComponent(userDraft.userName):"/api/management/accounts",editingUser?"PUT":"POST",
      {...userDraft,password});setPassword("");setUserDraft(emptyAccount);setEditingUser(false);await refresh();});
  }
  return <main className="management">
    <h1>工位管理与统计</h1>
    <button type="button" onClick={()=>onTrace()}>查看追溯台</button>
    <button type="button" disabled={busy} onClick={()=>void run(()=>refresh())}>刷新管理数据</button>
    {error && <p role="alert">{error}</p>}
    {busy && <p role="status">正在处理…</p>}
    {!me ? <form onSubmit={login}><h2>登录管理台</h2>
      <label>管理用户名 <input name="user" autoComplete="username" required maxLength={64}/></label>
      <label>管理密码 <input name="password" type="password" autoComplete="current-password" required maxLength={256}/></label>
      <button disabled={busy}>登录管理台</button>
    </form> : <>
      <p>当前账号 {me.user} · {me.role}</p>
      <button disabled={busy} onClick={()=>void run(async()=>{await write("/api/session/logout","POST");setPassword("");await refresh();})}>退出管理台</button>
      <section><h2>工位档案</h2>
        <table><caption>工位档案列表</caption><thead><tr><th>编码</th><th>名称</th><th>状态</th>{me.role==="admin" && <th>操作</th>}</tr></thead>
        <tbody>{stations.map(s=><tr key={s.code}><td>{s.code}</td><td>{s.name}</td><td>{s.enabled?"启用":"停用"}</td>
          {me.role==="admin" && <td><button disabled={busy} onClick={()=>{setDraft(s);setEditing(true);}}>编辑工位 {s.code}</button></td>}</tr>)}</tbody></table>
        {me.role==="admin" && <form onSubmit={saveStation}><h3>{editing?"编辑工位":"创建工位"}</h3>
          <label>工位编码 <input value={draft.code} disabled={editing||busy} required pattern="[a-z0-9][a-z0-9-]{0,63}" maxLength={64} onChange={e=>setDraft({...draft,code:e.target.value})}/></label>
          <label>工位名称 <input value={draft.name} disabled={busy} required maxLength={100} onChange={e=>setDraft({...draft,name:e.target.value})}/></label>
          {editing && <label><input type="checkbox" checked={draft.enabled} disabled={busy} onChange={e=>setDraft({...draft,enabled:e.target.checked})}/>工位启用</label>}
          <button disabled={busy}>保存工位</button>
          <button type="button" disabled={busy} onClick={()=>{setDraft(emptyStation);setEditing(false);}}>取消工位编辑</button>
          {editing && <button type="button" disabled={busy} onClick={()=>void run(async()=>{
            const latest=await get("/api/management/stations",z.array(stationSchema));setStations(latest);
            const found=latest.find(s=>s.code===draft.code);if(found)setDraft(found);else {setDraft(emptyStation);setEditing(false);}
          })}>放弃草稿并重读工位</button>}
        </form>}
      </section>
      {me.role==="admin" && <section><h2>用户与权限</h2>
        <table><caption>用户列表</caption><thead><tr><th>用户名</th><th>角色</th><th>状态</th><th>操作</th></tr></thead>
        <tbody>{accounts.map(a=><tr key={a.userName}><td>{a.userName}</td><td>{a.role}</td><td>{a.enabled?"启用":"停用"}</td>
          <td><button disabled={busy} onClick={()=>{setUserDraft(a);setPassword("");setEditingUser(true);}}>编辑用户 {a.userName}</button></td></tr>)}</tbody></table>
        <form onSubmit={saveUser}><h3>{editingUser?"编辑用户":"创建用户"}</h3>
          <label>新用户名 <input value={userDraft.userName} disabled={editingUser||busy} required maxLength={64} pattern="[a-z0-9][a-z0-9-]{0,63}" onChange={e=>setUserDraft({...userDraft,userName:e.target.value})}/></label>
          <label>账号角色 <select value={userDraft.role} disabled={busy} onChange={e=>setUserDraft({...userDraft,role:e.target.value as Account["role"]})}>
            <option value="viewer">查询员</option><option value="reviewer">复核员</option><option value="admin">管理员</option></select></label>
          <label>新密码{editingUser?"（留空不改）":""} <input type="password" autoComplete="new-password" value={password} minLength={12} maxLength={256} required={!editingUser} disabled={busy} onChange={e=>setPassword(e.target.value)}/></label>
          {editingUser && <label><input type="checkbox" checked={userDraft.enabled} disabled={busy} onChange={e=>setUserDraft({...userDraft,enabled:e.target.checked})}/>账号启用</label>}
          <button disabled={busy}>保存用户</button>
          <button type="button" disabled={busy} onClick={()=>{setUserDraft(emptyAccount);setPassword("");setEditingUser(false);}}>取消用户编辑</button>
        </form>
      </section>}
      <section><h2>质量统计</h2>
        <p>口径：导入时间 UTC；开始包含、结束不包含。停用工位的历史检测仍计入统计。</p>
        <label>统计工位 <select value={query.station} disabled={busy} onChange={e=>{const next={...query,station:e.target.value};setQuery(next);void run(()=>loadStats(next));}}>
          <option value="">全部</option>{stationCodes.map(code=><option key={code} value={code}>{code}</option>)}
        </select></label>
        <fieldset disabled={busy}><TimeRangeForm since={query.since} until={query.until} onApply={range=>{const next={...query,since:range.since,until:range.until};setQuery(next);void run(()=>loadStats(next));}}/></fieldset>
        {stats && <><button onClick={()=>onTrace({...query,result:"",reviewStatus:"",page:"",id:"",paging:""})}>查看统计范围明细</button><dl><dt>检测总数</dt><dd>{stats.total}</dd><dt>NG 数</dt><dd>{stats.ng}</dd><dt>待复核数</dt><dd>{stats.pending}</dd>
          <dt>NG 率</dt><dd>{stats.ngRate===null?"无数据":(stats.ngRate*100).toFixed(1)+"%"}</dd></dl>
          <table><caption>按工位统计</caption><thead><tr><th>工位</th><th>总数</th><th>NG</th><th>待复核</th><th>NG 比例</th></tr></thead>
          <tbody>{stats.stations.map(s=><tr key={s.station}><td>{s.station}</td><td>{s.total}</td><td>{s.ng}</td><td>{s.pending}</td>
            <td><meter min={0} max={1} value={s.ng/s.total} aria-label={s.station+" NG比例"}/>{(s.ng/s.total*100).toFixed(1)}%</td></tr>)}</tbody></table>
        </>}
      </section>
    </>}
  </main>;
}
