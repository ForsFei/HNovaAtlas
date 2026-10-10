"use client";

import Link from "next/link";
import { Search, X } from "lucide-react";
import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { excerptParts } from "@/lib/search/results";
import { useKnowledgeSearch } from "./useKnowledgeSearch";

export default function SearchDialog({ onClose }: { onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const input = useRef<HTMLInputElement>(null);
  const links = useRef<(HTMLAnchorElement | null)[]>([]);
  const [query, setQuery] = useState("");
  const [limit, setLimit] = useState(10);
  const [retry, setRetry] = useState(0);
  const state = useKnowledgeSearch(query, limit, retry);
  useEffect(() => {
    const modal = dialog.current;
    modal?.showModal();
    input.current?.focus();
    const overflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => { modal?.close(); document.body.style.overflow = overflow; };
  }, []);

  function move(event: KeyboardEvent, index: number) {
    if (event.nativeEvent.isComposing) return;
    if (event.key === "ArrowDown") { event.preventDefault(); links.current[(index + 1) % state.results.length]?.focus(); }
    if (event.key === "ArrowUp") { event.preventDefault(); if (index <= 0) input.current?.focus(); else links.current[index - 1]?.focus(); }
  }
  function trapTab(event: KeyboardEvent<HTMLDialogElement>) {
    if (event.key !== "Tab") return;
    const elements = Array.from(event.currentTarget.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), a[href]'));
    const first = elements[0];
    const last = elements.at(-1);
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
  }
  return <dialog ref={dialog} aria-labelledby="search-title" aria-describedby="search-hint" className="search-dialog" onKeyDown={trapTab} onCancel={(event) => { event.preventDefault(); onClose(); }} onClick={(event) => { if (event.target === event.currentTarget) { const rect = event.currentTarget.getBoundingClientRect(); if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) onClose(); } }}>
    <div className="flex items-center justify-between border-b border-border px-5 py-4"><h2 id="search-title" className="text-sm font-semibold">搜索 HNova Atlas</h2><button type="button" onClick={onClose} aria-label="关闭搜索" className="rounded p-1.5 text-secondary hover:bg-surface-hover"><X size={18} aria-hidden="true" /></button></div>
    <div className="flex items-center gap-3 border-b border-border px-5 py-4"><Search size={19} className="shrink-0 text-secondary" aria-hidden="true" /><label className="sr-only" htmlFor="knowledge-search">搜索关键词</label><input ref={input} id="knowledge-search" type="search" autoComplete="off" maxLength={200} placeholder="搜索标题、标签与正文…" value={query} onChange={(event) => { setQuery(event.target.value); setLimit(10); }} onKeyDown={(event) => {
      if (event.nativeEvent.isComposing || !state.results.length) return;
      if (event.key === "ArrowDown") { event.preventDefault(); links.current[0]?.focus(); }
      if (event.key === "ArrowUp") { event.preventDefault(); links.current[state.results.length - 1]?.focus(); }
      if (event.key === "Enter" && state.phase === "ready") { event.preventDefault(); links.current[0]?.click(); }
    }} className="min-w-0 flex-1 bg-transparent text-base text-primary outline-none" /></div>
    <div className="search-results" aria-busy={state.phase === "loading"}>
      <p className="px-5 py-3 text-xs text-secondary" role="status" aria-live="polite">{state.phase === "idle" ? "输入关键词，搜索知识文章。" : state.phase === "loading" ? "正在搜索…" : state.phase === "error" ? "搜索索引加载失败。请确认构建已完成，或重试。" : state.total ? `找到 ${state.total} 篇知识文章，已显示 ${state.results.length} 篇。` : `没有找到“${query.trim()}”，试试其他关键词。`}</p>
      {state.phase === "idle" && <p className="px-5 pb-6 text-sm leading-7 text-secondary">例如：PDO、MVVM、异步、对象字典。<br />支持中英文关键词，搜索仅包含已发布文章。</p>}
      {state.phase === "error" && <button type="button" onClick={() => setRetry((value) => value + 1)} className="mx-5 mb-5 rounded border border-border px-3 py-2 text-sm text-accent">重试搜索</button>}
      <ul className="px-2 pb-2">{state.results.map((result, index) => <li key={result.id}><Link ref={(element) => { links.current[index] = element; }} href={result.url} onClick={onClose} onKeyDown={(event) => move(event, index)} className="search-result block rounded-lg px-3 py-3 hover:bg-surface-hover focus:bg-accent-soft">
        <p className="mb-1 text-xs text-secondary">{result.category}</p><h3 className="text-sm font-semibold">{result.title}</h3><p className="mt-2 text-xs leading-6 text-secondary">{excerptParts(result.excerpt).map((part, position) => part.marked ? <mark key={position}>{part.text}</mark> : <span key={position}>{part.text}</span>)}</p>
      </Link></li>)}</ul>
      {state.phase === "ready" && state.results.length < state.total && <button type="button" onClick={() => setLimit((value) => value + 10)} className="mx-5 mb-5 rounded border border-border px-3 py-2 text-sm text-accent">加载更多结果</button>}
    </div>
    <p id="search-hint" className="border-t border-border px-5 py-3 text-xs leading-6 text-secondary">↑ ↓ 选择 · Enter 打开 · Esc 关闭{process.env.NODE_ENV === "development" && <span className="block text-warning">开发模式：仅检索标题、简介、标签与分类；完整正文搜索请 build 后 start。</span>}</p>
  </dialog>;
}
