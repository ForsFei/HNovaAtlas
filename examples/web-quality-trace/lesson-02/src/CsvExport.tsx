import { useEffect, useRef, useState } from "react";
import { httpError } from "./http-error";
import { saveFile } from "./save-file";

export function CsvExport({ url }: { url: string }) {
  const pending = useRef<AbortController | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => () => pending.current?.abort(), []);

  async function download() {
    if (pending.current) return;
    const controller = new AbortController();
    pending.current = controller;
    setBusy(true);
    setError("");
    try {
      const response = await fetch(url, { signal: controller.signal });
      if (!response.ok) {
        throw new Error(httpError(response, response.status === 422 ? "结果超过 1000 条，请缩小筛选范围。" : `导出失败（HTTP ${response.status}）。`));
      }
      const blob = await response.blob();
      if (controller.signal.aborted) return;
      saveFile(blob, "inspections.csv");
    } catch (error) {
      if (!controller.signal.aborted) setError(error instanceof Error ? error.message : "导出失败。");
    } finally {
      pending.current = null;
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  return <div>
    <button disabled={busy} onClick={() => void download()}>{busy ? "正在导出…" : "导出当前筛选结果 CSV"}</button>
    {error && <p role="alert">{error}</p>}
  </div>;
}
