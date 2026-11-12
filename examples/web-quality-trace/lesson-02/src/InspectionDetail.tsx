import { useJson } from "./useJson";
import { EvidenceDownload } from "./EvidenceDownload";
import { ReviewPanel } from "./ReviewPanel";
import { inspectionSchema, storageInspectionSchema, type Inspection } from "./inspection-schema";

export function InspectionDetail({ id, storageMode, reviewMode, onReviewSaved }: { storageMode: boolean; reviewMode: boolean; id: string; onReviewSaved: () => void }) {
  const inspectionPath = `/api/inspections/${encodeURIComponent(id)}`;
  const { load, retry } = useJson<Inspection>(inspectionPath, storageMode ? storageInspectionSchema : inspectionSchema);
  if (load.state === "loading") return <p role="status">正在加载详情…</p>;
  if (load.state === "error") return <div>
    <p role="alert">{load.message}</p>
    <button onClick={retry}>重新加载详情</button>
  </div>;

  const row = load.data;
  return <><dl>
    <dt>周期</dt><dd>{row.id}</dd>
    <dt>工位</dt><dd>{row.station}</dd>
    {row.inspectedAt && <><dt>检测时间</dt><dd><time dateTime={row.inspectedAt}>{row.inspectedAt}</time></dd></>}
    {row.importedAt && <><dt>导入时间</dt><dd><time dateTime={row.importedAt}>{row.importedAt}</time></dd></>}
    <dt>结果</dt><dd>{row.result}</dd>
    <dt>ROI 均值</dt><dd>{row.roiMean}</dd>
    {storageMode && <><dt>证据图</dt><dd>
      {/* eslint-disable-next-line @next/next/no-img-element -- This standalone Vite app uses the browser image loader. */}
      <img className="evidence" src={`${inspectionPath}/image`} alt={`周期 ${id} 的检测原图预览`} />
      <EvidenceDownload key={id} id={id} url={`${inspectionPath}/evidence`} />
    </dd></>}
  </dl>
    {reviewMode && row.result === "NG" && <ReviewPanel id={id} onSaved={onReviewSaved} />}
  </>;
}

