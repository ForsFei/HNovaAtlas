import { useEffect, useRef, useState } from "react";
import { httpError } from "./http-error";
import { saveFile } from "./save-file";

export function EvidenceDownload({ id, url }: { id: string; url: string }) {
  const pending = useRef<AbortController | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  useEffect(() => () => pending.current?.abort(), []);

  async function download() {
    if (pending.current) return;
    const controller = new AbortController();
    pending.current = controller;
    setBusy(true);
    setError("");
    setStatus("");
    try {
      if (!window.crypto.subtle)
        throw new Error("浏览器无法校验文件，请使用 HTTPS 或本机地址打开页面。");
      const response = await fetch(url, { signal: controller.signal, cache: "no-store" });
      if (response.status !== 200)
        throw new Error(httpError(response, `下载失败（HTTP ${response.status}），请重试。`));
      const digest = response.headers.get("Content-Digest")?.match(/^sha-256=:([A-Za-z0-9+/]{43}=):$/)?.[1];
      if (!digest || response.headers.get("Content-Type") !== "application/octet-stream")
        throw new Error(httpError(response, "下载响应格式不符合约定，请重试。"));

      const bytes = await response.arrayBuffer();
      if (controller.signal.aborted) return;
      const hash = await window.crypto.subtle.digest("SHA-256", bytes);
      if (controller.signal.aborted) return;
      const actual = btoa(String.fromCharCode(...new Uint8Array(hash)));
      if (actual !== digest)
        throw new Error(httpError(response, "文件摘要不匹配，请重试。"));
      saveFile(new Blob([bytes], { type: "application/octet-stream" }), `${id}.pgm`);
      setStatus("SHA-256 校验通过，已发起文件保存。");
    } catch (error) {
      if (!controller.signal.aborted)
        setError(error instanceof Error ? error.message : "下载失败，请重试。");
    } finally {
      pending.current = null;
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  return <div>
    <button disabled={busy} onClick={() => void download()}>{busy ? "正在下载并校验…" : "下载原始 PGM"}</button>
    {error && <p role="alert">{error}</p>}
    <div role="status">{status}</div>
  </div>;
}
