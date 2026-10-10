"use client";

import { useEffect, useState } from "react";
import type { TocItem } from "@/types/content";
import { lastHeadingAtOrBefore } from "@/lib/content/toc-position";

export function TableOfContents({ items, variant = "inline" }: { items: TocItem[]; variant?: "inline" | "sidebar" }) {
  const [active, setActive] = useState<string | undefined>(items[0]?.id);
  useEffect(() => {
    let frame = 0;
    const headings = items.map((item) => document.getElementById(item.id));
    const desktop = window.matchMedia("(min-width: 1280px)");
    function update() {
      frame = 0;
      if ((variant === "sidebar") !== desktop.matches) return;
      const index = lastHeadingAtOrBefore(headings.length, (position) => headings[position]?.getBoundingClientRect().top ?? Infinity, 140);
      let current: string | undefined = items[Math.max(0, index)]?.id;
      if (window.scrollY > 0 && window.scrollY + window.innerHeight >= document.documentElement.scrollHeight - 4) current = items.at(-1)?.id;
      setActive(current);
    }
    function schedule() { if (!frame) frame = requestAnimationFrame(update); }
    update();
    window.addEventListener("scroll", schedule, { passive: true });
    window.addEventListener("resize", schedule);
    return () => { cancelAnimationFrame(frame); window.removeEventListener("scroll", schedule); window.removeEventListener("resize", schedule); };
  }, [items, variant]);
  if (!items.length) return null;
  const links = <nav aria-label="本文目录" className="space-y-1 py-3">{items.map((item) => <a key={item.id} href={`#${item.id}`} aria-current={active === item.id ? "location" : undefined} onClick={() => setActive(item.id)} className={`block border-l-2 py-1.5 text-xs leading-5 ${item.depth === 3 ? "pl-5" : "pl-3"} ${active === item.id ? "border-accent text-accent" : "border-border text-secondary hover:text-primary"}`}>{item.title}</a>)}</nav>;
  return variant === "inline"
    ? <details className="panel mb-6 px-4"><summary className="py-3 text-sm font-medium">本文目录 <span className="text-secondary">· {items.length} 节</span></summary>{links}</details>
    : <aside><details open><summary className="text-xs font-semibold text-secondary">本文目录 <span aria-hidden="true">⌄</span></summary>{links}</details></aside>;
}
