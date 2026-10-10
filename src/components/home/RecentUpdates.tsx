import Link from "next/link";
import { ArrowUpRight } from "lucide-react";
import type { Article } from "@/types/content";

export function RecentUpdates({ articles }: { articles: Article[] }) {
  return <section id="recent" aria-labelledby="recent-title" className="mt-14">
    <div className="mb-6 flex flex-wrap items-end justify-between gap-3"><div><p className="eyebrow mb-2">Recently updated</p><h2 id="recent-title" className="text-2xl font-semibold tracking-tight">最近更新</h2></div><p className="text-xs text-secondary">知识持续修订，而非一次发布</p></div>
    {!articles.length ? <p className="panel p-6 text-sm text-secondary">暂时没有已发布的知识更新。</p> : <ul className="panel divide-y divide-border">{articles.map((article) => <li key={article.id}><Link href={article.href} className="group grid gap-3 rounded-lg p-5 hover:bg-surface-hover sm:grid-cols-[minmax(0,1fr)_auto] sm:items-center sm:p-6">
      <div className="min-w-0"><p className="mb-2 text-xs text-secondary">{article.breadcrumbs.slice(0, 2).map((item) => item.title).join(" / ")}</p><h3 className="text-base font-medium group-hover:text-accent">{article.title}</h3><p className="mt-1.5 text-sm leading-6 text-secondary">{article.description}</p></div>
      <div className="flex items-center gap-3 text-xs text-secondary"><span>更新于 <time dateTime={article.updated}>{article.updated}</time></span><ArrowUpRight size={15} className="group-hover:text-accent" aria-hidden="true" /></div>
    </Link></li>)}</ul>}
  </section>;
}
