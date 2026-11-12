import { useEffect, useState } from "react";
import { inspectionCursorPageSchema } from "./inspection-schema";
import { readJson } from "./read-json";

export function CursorUpdateHint({ firstUrl, loadedHeadId, revision }: {
  firstUrl: string;
  loadedHeadId: string | null;
  revision: number;
}) {
  const [changed, setChanged] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    void readJson(firstUrl, inspectionCursorPageSchema, controller.signal).then(latest => {
      if (!controller.signal.aborted)
        setChanged((latest.items[0]?.id ?? null) !== loadedHeadId);
    }, () => {
      // Keep the last observed status if a background check fails.
    });
    return () => controller.abort();
  }, [firstUrl, loadedHeadId, revision]);

  return <div role="status">{changed ? "当前筛选有更新。点击「刷新列表」查看。" : ""}</div>;
}
