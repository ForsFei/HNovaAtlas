"use client";

import { useEffect, useId, useRef, useState } from "react";
import { useTheme } from "next-themes";
import { validateMermaidSource } from "@/lib/mdx/mermaid-policy";

type Rendered = { key: string; svg?: string; error?: string };

export function Mermaid({ chart, caption = "流程图" }: { chart: string; caption?: string }) {
  const id = useId().replace(/[^a-zA-Z0-9_-]/g, "");
  const figure = useRef<HTMLElement>(null);
  const { resolvedTheme } = useTheme();
  const [visible, setVisible] = useState(false);
  const [retry, setRetry] = useState(0);
  const [rendered, setRendered] = useState<Rendered>({ key: "" });
  const key = `${chart}\n${resolvedTheme}\n${retry}`;
  const current = rendered.key === key ? rendered : undefined;

  useEffect(() => {
    if (!figure.current) return;
    if (!window.IntersectionObserver) {
      const frame = requestAnimationFrame(() => setVisible(true));
      return () => cancelAnimationFrame(frame);
    }
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) { setVisible(true); observer.disconnect(); }
    }, { rootMargin: "200px" });
    observer.observe(figure.current);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (!visible || !resolvedTheme) return;
    let cancelled = false;
    void Promise.resolve().then(() => {
      validateMermaidSource(chart);
      return import("@/lib/mdx/render-mermaid");
    }).then(({ renderMermaid }) => renderMermaid(`atlas-diagram-${id}-${retry}`, chart)).then((svg) => {
      if (!cancelled) setRendered({ key, svg });
    }).catch((error: unknown) => {
      if (!cancelled) setRendered({ key, error: error instanceof Error && /图表|当前仅支持/.test(error.message) ? error.message : "图表未能生成，请检查 Mermaid 语法或重试。" });
    });
    return () => { cancelled = true; };
  }, [chart, id, key, resolvedTheme, retry, visible]);

  return <figure ref={figure} className="atlas-diagram">
    <figcaption>{caption}</figcaption>
    <div data-pagefind-ignore>
      {current?.svg ? <div className="atlas-diagram-canvas" role="img" aria-label={caption} dangerouslySetInnerHTML={{ __html: current.svg }} /> : <p className="atlas-diagram-status" role="status">{current?.error ?? (visible ? "图表加载中…" : "图表将在滚动到此处时加载；也可查看源码。")}</p>}
      {current?.error && <button type="button" className="atlas-diagram-retry" onClick={() => setRetry((value) => value + 1)}>重试图表</button>}
    </div>
    <details className="atlas-diagram-source"><summary data-pagefind-ignore>查看图表源码</summary><pre><code>{chart}</code></pre></details>
  </figure>;
}
