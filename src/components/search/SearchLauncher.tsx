"use client";

import dynamic from "next/dynamic";
import { Search } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";

const SearchDialog = dynamic(() => import("./SearchDialog"), { ssr: false });

export function SearchLauncher() {
  const [open, setOpen] = useState(false);
  const returnFocus = useRef<HTMLElement | null>(null);
  const show = useCallback(() => { returnFocus.current = document.activeElement instanceof HTMLElement ? document.activeElement : null; setOpen(true); }, []);
  const close = useCallback(() => { setOpen(false); requestAnimationFrame(() => returnFocus.current?.focus()); }, []);
  useEffect(() => {
    function shortcut(event: KeyboardEvent) {
      if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.repeat && !event.isComposing && event.key.toLowerCase() === "k") {
        event.preventDefault();
        if (open) close(); else show();
      }
    }
    window.addEventListener("keydown", shortcut);
    return () => window.removeEventListener("keydown", shortcut);
  }, [open, close, show]);
  return <>
    <button type="button" onClick={show} aria-label="搜索知识" aria-haspopup="dialog" aria-expanded={open} aria-keyshortcuts="Control+k Meta+k" className="inline-flex shrink-0 items-center gap-2 rounded-lg border border-border bg-surface p-2.5 text-sm text-secondary hover:text-primary lg:min-w-48 lg:px-3">
      <Search size={17} aria-hidden="true" /><span className="hidden lg:inline">搜索知识…</span><kbd className="ml-auto hidden rounded border border-border px-1.5 py-0.5 text-[10px] lg:inline">⌘ / Ctrl K</kbd>
    </button>
    {open && <SearchDialog onClose={close} />}
  </>;
}
