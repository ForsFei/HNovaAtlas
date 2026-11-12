import { useCallback, useEffect, useRef, useState } from "react";
import { InspectionTable } from "./InspectionTable";
import { CursorUpdateHint } from "./CursorUpdateHint";
import { inspectionCursorPageSchema, type InspectionCursorPage } from "./inspection-schema";
import { readJson } from "./read-json";

export function CursorInspectionList({ firstUrl, liveRevision, onSelect }: {
  firstUrl: string;
  liveRevision?: number;
  onSelect: (id: string) => void;
}) {
  const [page, setPage] = useState<(InspectionCursorPage & { firstRead: number }) | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const pending = useRef<AbortController | null>(null);

  const request = useCallback((after: string | null) => {
    if (pending.current) return;
    const controller = new AbortController();
    pending.current = controller;

    const url = new URL(firstUrl, window.location.origin);
    if (after !== null) url.searchParams.set("after", after);
    void readJson(url.pathname + url.search, inspectionCursorPageSchema, controller.signal)
      .then(next => {
        if (pending.current !== controller) return;
        setPage(previous => ({
          items: after === null ? next.items : [...(previous?.items ?? []), ...next.items],
          nextCursor: next.nextCursor,
          firstRead: (previous?.firstRead ?? 0) + (after === null ? 1 : 0),
        }));
      })
      .catch(error => {
        if (pending.current === controller)
          setError(error instanceof Error ? error.message : "查询失败。");
      })
      .finally(() => {
        if (pending.current === controller) {
          pending.current = null;
          setLoading(false);
        }
      });
  }, [firstUrl]);

  useEffect(() => {
    request(null);
    return () => {
      pending.current?.abort();
      pending.current = null;
    };
  }, [request]);

  function requestFromButton(after: string | null) {
    if (pending.current) return;
    setLoading(true);
    setError("");
    request(after);
  }

  return <>
    {!!page?.items.length && <InspectionTable items={page.items}
      caption={`检测记录（已加载 ${page.items.length} 条）`} onSelect={onSelect} />}
    {liveRevision !== undefined && page && <CursorUpdateHint key={page.firstRead}
      firstUrl={firstUrl} loadedHeadId={page.items[0]?.id ?? null} revision={liveRevision} />}
    {error && <p role="alert">{error}</p>}
    <nav aria-label="游标分页" aria-busy={loading}>
      <button disabled={loading} onClick={() => requestFromButton(null)}>刷新列表</button>
      {page?.nextCursor && <button disabled={loading} onClick={() => requestFromButton(page.nextCursor)}>加载更多</button>}
      {loading ? <span role="status">正在加载记录…</span> : page?.nextCursor === null
        ? <span role="status">{page.items.length ? "已加载全部记录。" : "没有符合条件的记录。"}</span>
        : null}
    </nav>
  </>;
}
