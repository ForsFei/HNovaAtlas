import { useJson } from "./useJson";
import { InspectionTable } from "./InspectionTable";
import { inspectionArraySchema, inspectionPageSchema, type Inspection, type InspectionPage } from "./inspection-schema";

export function InspectionList({ url, result, page, revision, storageMode, pageSize, onPage, onRecoverPage, onSelect }: {
  storageMode: boolean;
  pageSize: number;
  url: string;
  result: string;
  page: number;
  revision: number;
  onPage: (page: number) => void;
  onRecoverPage: (page: number) => void;
  onSelect: (id: string) => void;
}) {
  const { load, retry } = useJson<Inspection[] | InspectionPage>(url, storageMode ? inspectionPageSchema : inspectionArraySchema, revision);
  if (load.state === "loading") return <p role="status">正在加载记录…</p>;
  if (load.state === "error") return <div>
    <p role="alert">{load.message}</p>
    <button onClick={retry}>重新加载记录</button>
  </div>;

  const data = load.data;
  let view: { items: Inspection[]; total: number };
  if (Array.isArray(data)) {
    const filtered = data.filter(row => !result || row.result === result);
    view = {
      items: filtered.slice((page - 1) * pageSize, page * pageSize),
      total: filtered.length,
    };
  } else {
    view = data;
  }
  const { items, total } = view;
  const totalPages = Math.ceil(total / pageSize);
  if (!totalPages) return <div>
    <p role="status">没有符合条件的记录。</p>
    {page > 1 && <button onClick={() => onRecoverPage(1)}>回到第 1 页</button>}
  </div>;
  if (page > totalPages) return <div>
    <p role="status">记录已变化，当前只有 {totalPages} 页。</p>
    <button onClick={() => onRecoverPage(totalPages)}>回到最后一页</button>
    {totalPages > 1 && <button onClick={() => onRecoverPage(1)}>回到第 1 页</button>}
  </div>;

  return <>
    <InspectionTable items={items} caption={`检测记录（共 ${total} 条）`} onSelect={onSelect} />
    <nav aria-label="检测记录分页">
      <button disabled={page === 1} onClick={() => onPage(page - 1)}>上一页</button>
      <span>第 {page} / {totalPages} 页</span>
      <button disabled={page === totalPages} onClick={() => onPage(page + 1)}>下一页</button>
    </nav>
  </>;
}

