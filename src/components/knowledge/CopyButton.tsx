"use client";

import { useEffect, useRef, useState } from "react";

export function CopyButton({ code }: { code: string }) {
  const [status, setStatus] = useState("复制");
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  useEffect(() => () => clearTimeout(timer.current), []);
  async function copy() {
    clearTimeout(timer.current);
    try { await navigator.clipboard.writeText(code); setStatus("已复制"); }
    catch { setStatus("复制失败，请手动选择"); }
    timer.current = setTimeout(() => setStatus("复制"), 2400);
  }
  return <button type="button" onClick={copy} aria-label="复制代码" className="rounded px-2 py-1 text-xs text-secondary hover:bg-surface-hover hover:text-primary"><span aria-live="polite">{status}</span></button>;
}
