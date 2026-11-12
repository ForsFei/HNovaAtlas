/// <reference types="vite/client" />

import { StrictMode, useState } from "react";
import { createRoot } from "react-dom/client";
import { useInspectionFeed } from "./useInspectionFeed";
import { TimeRangeForm } from "./TimeRangeForm";
import { CsvExport } from "./CsvExport";
import { CursorInspectionList } from "./CursorInspectionList";
import { inspectionCursorUrl, inspectionUrl } from "./inspection-query";
import { InspectionList } from "./InspectionList";
import { InspectionDetail } from "./InspectionDetail";
import { useInspectionNavigation } from "./useInspectionNavigation";
import "./style.css";

const liveMode = import.meta.env.MODE === "live";
const reviewMode = liveMode || import.meta.env.MODE === "review";
const storageMode = reviewMode || import.meta.env.MODE === "storage";
const pageSizes = [1, 10, 20, 50];

function App() {
  const feed = useInspectionFeed(liveMode);
  const { search, navigate } = useInspectionNavigation();
  const [reviewRevision, setReviewRevision] = useState(0);
  const [filterResetRevision, setFilterResetRevision] = useState(0);

  const params = new URLSearchParams(search);
  const requestedSize = params.get("pageSize") ?? "1";
  const pageSize = pageSizes.some(size => String(size) === requestedSize) ? Number(requestedSize) : 1;
  const station = params.get("station") ?? "";
  const result = params.get("result") ?? "";
  const rawPage = Number(params.get("page") ?? 1);
  const page = Number.isSafeInteger(rawPage) && rawPage >= 1 && rawPage <= 2_147_483_647 ? rawPage : 1;
  const id = params.get("id") ?? "";
  const since = params.get("since") ?? "";
  const until = params.get("until") ?? "";
  const reviewStatus = reviewMode ? params.get("reviewStatus") ?? "" : "";
  const reviewRefresh = reviewStatus ? reviewRevision : 0;
  const cursorMode = storageMode && params.get("paging") === "cursor";
  const cursorUrl = cursorMode && station.trim() && (result === "OK" || result === "NG")
    ? inspectionCursorUrl({ station, result, since, until, pageSize, reviewStatus }) : null;
  const filters = storageMode ? { station, result, since, until, reviewStatus: reviewStatus || undefined, newest: liveMode || cursorMode || undefined } : { station };
  const listUrl = inspectionUrl(storageMode ? { ...filters, page, pageSize } : filters);
  const csvUrl = inspectionUrl({ ...filters, format: "csv" });


  return <main>
    <h1>工业工位质量追溯台</h1>
    <section aria-labelledby="list-title">
      <h2 id="list-title">检测记录</h2>
      {liveMode && <p role="status">
        {feed.status === "connected" ? "实时连接正常" : "正在连接实时更新…"}
      </p>}
      <div className="filters">
        <label>每页条数 <select value={pageSize} onChange={event => navigate({
          pageSize: event.target.value === "1" ? "" : event.target.value, page: "", id: "",
        })}>
          {pageSizes.map(size => <option key={size} value={size}>{size} 条</option>)}
        </select></label>
        <label>工位 <select value={station} onChange={event => navigate({ station: event.target.value, page: "", id: "" })}>
          <option value="">全部</option>
          <option value="station-a">station-a</option>
          <option value="station-b">station-b</option>
          {station && !["station-a", "station-b"].includes(station) && <option value={station}>{station}</option>}
        </select></label>
        <label>结果 <select value={result} disabled={!!reviewStatus} onChange={event => navigate({ result: event.target.value, page: "", id: "" })}>
          <option value="">全部</option>
          <option value="OK">OK</option>
          <option value="NG">NG</option>
          {result && !["OK", "NG"].includes(result) && <option value={result}>{result}</option>}
        </select></label>
        {reviewMode && <label>复核状态 <select value={reviewStatus}
          onChange={event => navigate({ reviewStatus: event.target.value,
            result: event.target.value ? "NG" : result, page: "", id: "" })}>
          <option value="">全部</option>
          <option value="pending">待复核</option>
          <option value="reviewed">已复核</option>
          {reviewStatus && !["pending", "reviewed"].includes(reviewStatus) && <option value={reviewStatus}>{reviewStatus}</option>}
        </select></label>}
        {storageMode && <label>分页方式 <select value={cursorMode ? "cursor" : "page"}
          onChange={event => navigate({ paging: event.target.value === "cursor" ? "cursor" : "", page: "", id: "" })}>
          <option value="page">按页码</option>
          <option value="cursor">加载更多</option>
        </select></label>}
        <button type="button" onClick={() => {
          navigate({ station: "", result: "", since: "", until: "", reviewStatus: "", page: "", id: "" });
          setFilterResetRevision(value => value + 1);
        }}>清除筛选</button>
      </div>
      {storageMode && <TimeRangeForm key={JSON.stringify([since, until, filterResetRevision])} since={since} until={until} onApply={navigate} />}
      {storageMode && <CsvExport key={csvUrl} url={csvUrl} />}
      {reviewStatus && <button onClick={() => setReviewRevision(value => value + 1)}>刷新复核列表</button>}
      {cursorMode ? (cursorUrl
        ? <CursorInspectionList key={JSON.stringify([cursorUrl, reviewRefresh])} firstUrl={cursorUrl} liveRevision={liveMode ? feed.revision : undefined}
          onSelect={value => navigate({ id: value })} />
        : <p role="status">请选择一个工位和 OK 或 NG 结果。</p>)
        : <InspectionList storageMode={storageMode} pageSize={pageSize} key={listUrl} url={listUrl} result={result} page={page} revision={feed.revision + reviewRefresh}
          onPage={value => navigate({ page: value === 1 ? "" : String(value), id: "" })}
          onRecoverPage={value => navigate({ page: value === 1 ? "" : String(value) })}
          onSelect={value => navigate({ id: value })} />}
    </section>
    {id && <section aria-labelledby="detail-title">
      <h2 id="detail-title">周期详情</h2>
      <button onClick={() => navigate({ id: "" })}>关闭详情</button>
      <InspectionDetail storageMode={storageMode} reviewMode={reviewMode} key={id} id={id} onReviewSaved={() => setReviewRevision(value => value + 1)} />
    </section>}
  </main>;
}

createRoot(document.getElementById("root")!).render(<StrictMode><App /></StrictMode>);
