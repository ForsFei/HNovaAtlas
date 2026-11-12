import { z } from "zod";
import { useJson } from "./useJson";

const codesSchema = z.array(z.string());
export function ManagedStationFilter({value,onChange}:{value:string;onChange:(value:string)=>void}) {
  const {load,retry}=useJson("/api/management/station-codes",codesSchema);
  if(load.state==="loading") return <p role="status">正在读取工位目录…</p>;
  if(load.state==="error") return <div><p role="alert">{load.message}</p><button onClick={retry}>重读工位目录</button></div>;
  const codes=value && !load.data.includes(value) ? [...load.data,value] : load.data;
  return <label>工位 <select value={value} onChange={e=>onChange(e.target.value)}>
    <option value="">全部</option>{codes.map(code=><option key={code} value={code}>{code}</option>)}
  </select></label>;
}
